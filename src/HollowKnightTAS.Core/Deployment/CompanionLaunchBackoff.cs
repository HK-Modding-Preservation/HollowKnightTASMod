using System;
using System.Collections.Generic;

namespace HollowKnightTAS.Core.Deployment
{
    public sealed class CompanionBackoffDecision
    {
        internal CompanionBackoffDecision(
            bool circuitOpen,
            TimeSpan delay,
            int recentFailureCount)
        {
            CircuitOpen = circuitOpen;
            Delay = delay;
            RecentFailureCount = recentFailureCount;
        }

        public bool CircuitOpen { get; }
        public TimeSpan Delay { get; }
        public int RecentFailureCount { get; }
    }

    public sealed class CompanionLaunchBackoff
    {
        private static readonly TimeSpan[] Delays =
        {
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(30)
        };

        private readonly Queue<DateTimeOffset> failures =
            new Queue<DateTimeOffset>();

        public CompanionBackoffDecision RegisterFailure(
            DateTimeOffset now)
        {
            var cutoff = now - TimeSpan.FromMinutes(5);
            while (failures.Count > 0
                   && failures.Peek() < cutoff)
            {
                failures.Dequeue();
            }

            failures.Enqueue(now);
            var count = failures.Count;
            return new CompanionBackoffDecision(
                count >= 5,
                Delays[Math.Min(count - 1, Delays.Length - 1)],
                count);
        }

        public void Reset()
        {
            failures.Clear();
        }
    }
}
