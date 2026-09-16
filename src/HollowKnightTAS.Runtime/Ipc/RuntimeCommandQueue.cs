using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using HollowKnightTAS.Core.Ipc;

namespace HollowKnightTAS.Runtime.Ipc
{
    public sealed class ValidatedRuntimeCommand
    {
        public ValidatedRuntimeCommand(
            long sequence,
            string messageType,
            IReadOnlyDictionary<string, string> fields)
        {
            Sequence = sequence;
            MessageType = messageType
                          ?? throw new ArgumentNullException(
                              nameof(messageType));
            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }

            var copy = new SortedDictionary<string, string>(
                StringComparer.Ordinal);
            foreach (var pair in fields)
            {
                copy.Add(pair.Key, pair.Value);
            }

            Fields = new ReadOnlyDictionary<string, string>(copy);
        }

        public long Sequence { get; }
        public string MessageType { get; }
        public IReadOnlyDictionary<string, string> Fields { get; }
    }

    public sealed class RuntimeCommandQueue
    {
        private readonly BoundedIpcQueue<ValidatedRuntimeCommand> queue;
        private readonly AutoResetEvent activity = new AutoResetEvent(false);

        public RuntimeCommandQueue(int capacity)
        {
            queue = new BoundedIpcQueue<ValidatedRuntimeCommand>(
                capacity);
        }

        public int Count => queue.Count;
        public long RejectedCount => queue.RejectedCount;

        public bool TryEnqueue(ValidatedRuntimeCommand command)
        {
            var accepted = queue.TryEnqueue(command);
            if (accepted)
            {
                activity.Set();
            }

            return accepted;
        }

        public bool TryDequeue(out ValidatedRuntimeCommand command)
        {
            return queue.TryDequeue(out command!);
        }

        public void Clear()
        {
            queue.Clear();
            activity.Set();
        }

        public bool WaitForActivity(TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero
                && timeout != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            return queue.Count > 0 || activity.WaitOne(timeout);
        }
    }
}
