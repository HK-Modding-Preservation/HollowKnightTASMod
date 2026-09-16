using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using HollowKnightTAS.Core.Diagnostics;

namespace HollowKnightTAS.Core.Ledger
{
    public sealed class TickLedgerJsonlSink : IDisposable
    {
        private readonly BlockingCollection<QueueItem> queue;
        private readonly Thread writerThread;
        private readonly StreamWriter writer;
        private readonly TimeSpan shutdownTimeout;
        private readonly long maximumBytes;
        private readonly string incompletePath;
        private readonly DirectoryLogBudget? directoryBudget;
        private int sizeLimitReached;
        private Exception? writerFailure;
        private long droppedCount;
        private int disposed;

        public TickLedgerJsonlSink(
            string path,
            int capacity,
            TimeSpan shutdownTimeout,
            long maximumBytes = 64L * 1024 * 1024,
            string? budgetDirectory = null,
            long maximumDirectoryBytes = DirectoryLogBudget.DefaultMaximumBytes)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A ledger path is required.", nameof(path));
            }

            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (shutdownTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(shutdownTimeout));
            }

            if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            incompletePath = Path.GetFullPath(path) + ".incomplete";
            if (File.Exists(incompletePath)) throw new IOException("Incomplete ledger already exists.");
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            this.shutdownTimeout = shutdownTimeout;
            directoryBudget = budgetDirectory == null ? null
                : DirectoryLogBudget.Reserve(budgetDirectory, path, maximumBytes, maximumDirectoryBytes);
            this.maximumBytes = directoryBudget?.GrantedBytes ?? maximumBytes;
            queue = new BlockingCollection<QueueItem>(capacity);
            try
            {
                writer = new StreamWriter(
                    new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
                    new UTF8Encoding(false, true)) { NewLine = "\n" };
                try
                {
                    using (var marker = new StreamWriter(new FileStream(incompletePath,
                        FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)))
                        marker.Write("Ledger is open, failed, or contains dropped records.\n");
                }
                catch { writer.Dispose(); throw; }
            }
            catch { directoryBudget?.Dispose(); queue.Dispose(); throw; }
            writerThread = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "HollowKnightTAS Tick Ledger Writer"
            };
            writerThread.Start();
        }

        public long DroppedCount => Interlocked.Read(ref droppedCount);
        public bool SizeLimitReached => Volatile.Read(ref sizeLimitReached) != 0;

        public bool TryEmit(TickLedgerRecord value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            ThrowIfDisposed();
            ThrowIfWriterFailed();
            if (SizeLimitReached)
            {
                Interlocked.Increment(ref droppedCount);
                return false;
            }
            if (queue.TryAdd(QueueItem.ForRecord(value)))
            {
                return true;
            }

            Interlocked.Increment(ref droppedCount);
            return false;
        }

        public void Flush(TimeSpan timeout)
        {
            ThrowIfDisposed();
            ThrowIfWriterFailed();
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            using (var signal = new ManualResetEventSlim(false))
            {
                var milliseconds = ToBoundedMilliseconds(timeout);
                if (!queue.TryAdd(QueueItem.ForFlush(signal), milliseconds))
                {
                    throw new TimeoutException("Timed out while enqueueing a ledger flush.");
                }

                if (!signal.Wait(milliseconds))
                {
                    throw new TimeoutException("Timed out while flushing the tick ledger.");
                }
            }

            ThrowIfWriterFailed();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            queue.CompleteAdding();
            writerThread.Join(ToBoundedMilliseconds(shutdownTimeout));
            if (!writerThread.IsAlive)
            {
                queue.Dispose();
            }
        }

        private void WriterLoop()
        {
            long previousSequence = 0;
            long writtenBytes = 0;
            var encoding = new UTF8Encoding(false, true);
            try
            {
                foreach (var item in queue.GetConsumingEnumerable())
                {
                    if (item.Record != null)
                    {
                        if (item.Record.Sequence <= previousSequence)
                        {
                            throw new InvalidDataException(
                                "Ledger sequence must be strictly increasing.");
                        }

                        previousSequence = item.Record.Sequence;
                        var json = TickLedgerRecordJson.Serialize(item.Record);
                        var bytes = (long)encoding.GetByteCount(json) + 1;
                        if (SizeLimitReached || bytes > maximumBytes - writtenBytes)
                        {
                            Volatile.Write(ref sizeLimitReached, 1);
                            Interlocked.Increment(ref droppedCount);
                            continue;
                        }
                        writer.WriteLine(json);
                        writtenBytes += bytes;
                    }
                    else if (item.FlushSignal != null)
                    {
                        writer.Flush();
                        item.FlushSignal.Set();
                    }
                }

                writer.Flush();
            }
            catch (Exception exception)
            {
                writerFailure = exception;
                while (queue.TryTake(out var pending))
                {
                    pending.FlushSignal?.Set();
                }
            }
            finally
            {
                try { writer.Dispose(); }
                catch (Exception exception) { writerFailure = exception; }
                try
                {
                    if (writerFailure == null && DroppedCount == 0)
                        File.Delete(incompletePath);
                }
                catch (Exception exception) { writerFailure = exception; }
                try { directoryBudget?.Dispose(); }
                catch (Exception exception) { writerFailure = exception; }
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(TickLedgerJsonlSink));
            }
        }

        private void ThrowIfWriterFailed()
        {
            var failure = writerFailure;
            if (failure != null)
            {
                throw new IOException("The tick ledger writer failed.", failure);
            }
        }

        private static int ToBoundedMilliseconds(TimeSpan value)
        {
            return (int)Math.Max(
                1,
                Math.Min(int.MaxValue, Math.Ceiling(value.TotalMilliseconds)));
        }

        private sealed class QueueItem
        {
            private QueueItem(
                TickLedgerRecord? record,
                ManualResetEventSlim? flushSignal)
            {
                Record = record;
                FlushSignal = flushSignal;
            }

            public TickLedgerRecord? Record { get; }
            public ManualResetEventSlim? FlushSignal { get; }

            public static QueueItem ForRecord(TickLedgerRecord value)
            {
                return new QueueItem(value, null);
            }

            public static QueueItem ForFlush(ManualResetEventSlim signal)
            {
                return new QueueItem(null, signal);
            }
        }
    }
}
