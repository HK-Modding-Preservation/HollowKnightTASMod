using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using HollowKnightTAS.Core.Diagnostics;
using HollowKnightTAS.Core.Inspector;

namespace HollowKnightTAS.Runtime.Inspector
{
    public sealed class WatchJsonLinesExporter : IDisposable
    {
        private readonly BlockingCollection<QueueItem> queue;
        private readonly StreamWriter writer;
        private readonly Thread writerThread;
        private readonly TimeSpan shutdownTimeout;
        private readonly Action<long> onPressure;
        private readonly long maximumBytes;
        private readonly string incompletePath;
        private readonly DirectoryLogBudget? directoryBudget;
        private int sizeLimitReached;
        private Exception? writerFailure;
        private long droppedCount;
        private long writtenCount;
        private long lastEnqueuedSequence;
        private int paused;
        private int disposed;

        public WatchJsonLinesExporter(
            string path,
            int capacity,
            TimeSpan shutdownTimeout,
            Action<long> onPressure,
            long maximumBytes = 64L * 1024 * 1024,
            string? budgetDirectory = null,
            long maximumDirectoryBytes = DirectoryLogBudget.DefaultMaximumBytes)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "An export path is required.",
                    nameof(path));
            }

            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (shutdownTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(shutdownTimeout));
            }

            if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            incompletePath = Path.GetFullPath(path) + ".incomplete";
            if (File.Exists(incompletePath)) throw new IOException("Incomplete watch export already exists.");
            var directory = Path.GetDirectoryName(
                Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            this.shutdownTimeout = shutdownTimeout;
            this.onPressure = onPressure
                              ?? throw new ArgumentNullException(
                                  nameof(onPressure));
            queue = new BlockingCollection<QueueItem>(capacity);
            directoryBudget = budgetDirectory == null ? null
                : DirectoryLogBudget.Reserve(budgetDirectory, path, maximumBytes, maximumDirectoryBytes);
            this.maximumBytes = directoryBudget?.GrantedBytes ?? maximumBytes;
            try
            {
                writer = new StreamWriter(
                    new FileStream(
                        path,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.Read),
                    new UTF8Encoding(false, true))
                {
                    NewLine = "\n"
                };
                try
                {
                    using (var marker = new StreamWriter(new FileStream(incompletePath,
                        FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)))
                        marker.Write("Watch export is open, failed, or contains dropped records.\n");
                }
                catch { writer.Dispose(); throw; }
            }
            catch { directoryBudget?.Dispose(); queue.Dispose(); throw; }
            writerThread = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "HollowKnightTAS Watch JSONL Writer"
            };
            writerThread.Start();
        }

        public bool Paused => Volatile.Read(ref paused) != 0;
        public bool SizeLimitReached => Volatile.Read(ref sizeLimitReached) != 0;
        public long DroppedCount => Interlocked.Read(ref droppedCount);
        public long WrittenCount => Interlocked.Read(ref writtenCount);
        public long LastEnqueuedSequence =>
            Interlocked.Read(ref lastEnqueuedSequence);

        public void SetPaused(bool value)
        {
            Volatile.Write(ref paused, value ? 1 : 0);
        }

        public bool TryExport(WatchFrame frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            ThrowIfDisposed();
            ThrowIfWriterFailed();
            if (Paused)
            {
                return false;
            }

            if (SizeLimitReached)
            {
                Interlocked.Increment(ref droppedCount);
                return false;
            }

            var json = WatchFrameJson.Serialize(frame);
            if (!queue.TryAdd(QueueItem.ForLine(json)))
            {
                var dropped = Interlocked.Increment(
                    ref droppedCount);
                onPressure(dropped);
                return false;
            }

            Interlocked.Exchange(
                ref lastEnqueuedSequence,
                frame.Sequence);
            return true;
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
                var milliseconds = ToMilliseconds(timeout);
                if (!queue.TryAdd(
                        QueueItem.ForFlush(signal),
                        milliseconds))
                {
                    throw new TimeoutException(
                        "Timed out enqueueing watch export flush.");
                }

                if (!signal.Wait(milliseconds))
                {
                    throw new TimeoutException(
                        "Timed out flushing watch export.");
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
            writerThread.Join(ToMilliseconds(shutdownTimeout));
            if (!writerThread.IsAlive)
            {
                queue.Dispose();
            }
        }

        private void WriterLoop()
        {
            try
            {
                long writtenBytes = 0;
                var encoding = new UTF8Encoding(false, true);
                foreach (var item in queue.GetConsumingEnumerable())
                {
                    if (item.Line != null)
                    {
                        var bytes = (long)encoding.GetByteCount(item.Line) + 1;
                        if (SizeLimitReached || bytes > maximumBytes - writtenBytes)
                        {
                            Volatile.Write(ref sizeLimitReached, 1);
                            Interlocked.Increment(ref droppedCount);
                            continue;
                        }
                        writer.WriteLine(item.Line);
                        writtenBytes += bytes;
                        Interlocked.Increment(ref writtenCount);
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
                // Only a clean close with no lost records makes this complete evidence.
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
                throw new ObjectDisposedException(
                    nameof(WatchJsonLinesExporter));
            }
        }

        private void ThrowIfWriterFailed()
        {
            if (writerFailure != null)
            {
                throw new IOException(
                    "The watch JSONL writer failed.",
                    writerFailure);
            }
        }

        private static int ToMilliseconds(TimeSpan value)
        {
            return (int)Math.Max(
                1,
                Math.Min(
                    int.MaxValue,
                    Math.Ceiling(value.TotalMilliseconds)));
        }

        private sealed class QueueItem
        {
            private QueueItem(
                string? line,
                ManualResetEventSlim? flushSignal)
            {
                Line = line;
                FlushSignal = flushSignal;
            }

            public string? Line { get; }
            public ManualResetEventSlim? FlushSignal { get; }

            public static QueueItem ForLine(string line)
            {
                return new QueueItem(line, null);
            }

            public static QueueItem ForFlush(
                ManualResetEventSlim signal)
            {
                return new QueueItem(null, signal);
            }
        }
    }
}
