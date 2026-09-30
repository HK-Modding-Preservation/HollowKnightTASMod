using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace HollowKnightTAS.Core.Diagnostics
{
    public sealed class JsonLinesEventSink : IEventSink
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

        public JsonLinesEventSink(
            string path,
            int capacity,
            TimeSpan shutdownTimeout,
            long maximumBytes = 64L * 1024 * 1024,
            string? budgetDirectory = null,
            long maximumDirectoryBytes = DirectoryLogBudget.DefaultMaximumBytes)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("An event file path is required.", nameof(path));
            }

            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (shutdownTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(shutdownTimeout));
            }

            if (maximumBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }
            directoryBudget = budgetDirectory == null ? null
                : DirectoryLogBudget.Reserve(budgetDirectory, path, maximumBytes, maximumDirectoryBytes);
            this.maximumBytes = directoryBudget?.GrantedBytes ?? maximumBytes;
            incompletePath = Path.GetFullPath(path) + ".incomplete";
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            this.shutdownTimeout = shutdownTimeout;
            queue = new BlockingCollection<QueueItem>(capacity);
            try
            {
                writer = new StreamWriter(
                    new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
                    new UTF8Encoding(false, true));
            }
            catch { directoryBudget?.Dispose(); queue.Dispose(); throw; }
            writer.NewLine = "\n";
            writerThread = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "HollowKnightTAS JSONL Writer"
            };
            writerThread.Start();
        }

        public long DroppedCount => Interlocked.Read(ref droppedCount);
        public bool SizeLimitReached => Volatile.Read(ref sizeLimitReached) != 0;

        public void Emit(StructuredEvent value)
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
                return;
            }
            if (!queue.TryAdd(QueueItem.ForEvent(value)))
            {
                Interlocked.Increment(ref droppedCount);
            }
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
                    throw new TimeoutException("Timed out while enqueueing the JSONL flush barrier.");
                }

                if (!signal.Wait(milliseconds))
                {
                    throw new TimeoutException("Timed out while flushing the JSONL writer.");
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
            try
            {
                var sinceFlush = Stopwatch.StartNew();
                var dirty = false;
                while (!queue.IsCompleted)
                {
                    // Flush on wall time even while idle or continuously busy. Protected
                    // restarts kill Unity, so its normal shutdown flush is not guaranteed.
                    if (dirty && sinceFlush.ElapsedMilliseconds >= 250)
                    {
                        writer.Flush();
                        MarkDroppedStream();
                        dirty = false;
                        sinceFlush.Restart();
                    }
                    if (!queue.TryTake(out var item, 250)) continue;
                    if (item.Event != null)
                    {
                        if (item.Event.Sequence <= previousSequence)
                        {
                            throw new InvalidDataException("Structured event sequence must be strictly increasing.");
                        }

                        previousSequence = item.Event.Sequence;
                        var json = StructuredEventJson.Serialize(item.Event);
                        var bytes = (long)writer.Encoding.GetByteCount(json) + 1;
                        if (SizeLimitReached || bytes > maximumBytes - writtenBytes)
                        {
                            if (!SizeLimitReached)
                            {
                                File.WriteAllText(incompletePath,
                                    "Event log size limit reached; this event stream is incomplete.\n",
                                    new UTF8Encoding(false));
                                Volatile.Write(ref sizeLimitReached, 1);
                            }
                            Interlocked.Increment(ref droppedCount);
                            continue;
                        }
                        writer.WriteLine(json);
                        dirty = true;
                        writtenBytes += bytes;
                    }
                    else if (item.FlushSignal != null)
                    {
                        writer.Flush();
                        dirty = false;
                        sinceFlush.Restart();
                        MarkDroppedStream();
                        item.FlushSignal.Set();
                    }
                }

                writer.Flush();
                MarkDroppedStream();
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
                try { directoryBudget?.Dispose(); }
                catch (Exception exception) { writerFailure = exception; }
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(JsonLinesEventSink));
            }
        }

        private void MarkDroppedStream()
        {
            if (DroppedCount > 0 && !File.Exists(incompletePath))
                File.WriteAllText(incompletePath,
                    "Events were dropped; this event stream is incomplete.\n", new UTF8Encoding(false));
        }

        private void ThrowIfWriterFailed()
        {
            var failure = writerFailure;
            if (failure != null)
            {
                throw new IOException("The JSONL writer failed.", failure);
            }
        }

        private static int ToBoundedMilliseconds(TimeSpan value)
        {
            return (int)Math.Max(1, Math.Min(int.MaxValue, Math.Ceiling(value.TotalMilliseconds)));
        }

        private sealed class QueueItem
        {
            private QueueItem(StructuredEvent? value, ManualResetEventSlim? flushSignal)
            {
                Event = value;
                FlushSignal = flushSignal;
            }

            public StructuredEvent? Event { get; }
            public ManualResetEventSlim? FlushSignal { get; }

            public static QueueItem ForEvent(StructuredEvent value)
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
