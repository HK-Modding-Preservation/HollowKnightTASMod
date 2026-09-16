using System;
using System.Collections.Generic;

namespace HollowKnightTAS.Core.Ipc
{
    public sealed class BoundedIpcQueue<T>
    {
        private readonly object sync = new object();
        private readonly Queue<T> queue;

        public BoundedIpcQueue(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Capacity = capacity;
            queue = new Queue<T>(capacity);
        }

        public int Capacity { get; }

        public int Count
        {
            get
            {
                lock (sync)
                {
                    return queue.Count;
                }
            }
        }

        public long RejectedCount { get; private set; }

        public bool TryEnqueue(T value)
        {
            lock (sync)
            {
                if (queue.Count >= Capacity)
                {
                    RejectedCount++;
                    return false;
                }

                queue.Enqueue(value);
                return true;
            }
        }

        public bool TryDequeue(out T value)
        {
            lock (sync)
            {
                if (queue.Count == 0)
                {
                    value = default!;
                    return false;
                }

                value = queue.Dequeue();
                return true;
            }
        }

        public void Clear()
        {
            lock (sync)
            {
                queue.Clear();
            }
        }
    }
}
