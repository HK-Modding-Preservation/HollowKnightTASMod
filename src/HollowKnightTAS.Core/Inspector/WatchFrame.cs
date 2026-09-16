using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HollowKnightTAS.Core.Ledger;

namespace HollowKnightTAS.Core.Inspector
{
    public sealed class WatchFrameEntry
    {
        internal WatchFrameEntry(
            WatchDescriptor descriptor,
            WatchValue value,
            TickStamp sampledAt,
            long sampledAtMovieTick,
            bool fresh,
            long ageMovieTicks)
        {
            Descriptor = descriptor;
            Value = value;
            SampledAt = sampledAt;
            SampledAtMovieTick = sampledAtMovieTick;
            IsFresh = fresh;
            AgeMovieTicks = ageMovieTicks;
        }

        public WatchDescriptor Descriptor { get; }
        public WatchValue Value { get; }
        public TickStamp SampledAt { get; }
        public long SampledAtMovieTick { get; }
        public bool IsFresh { get; }
        public long AgeMovieTicks { get; }
    }

    public sealed class WatchProviderFailure
    {
        public WatchProviderFailure(
            string providerId,
            string error)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException(
                    "A provider ID is required.",
                    nameof(providerId));
            }

            ProviderId = providerId;
            Error = error
                    ?? throw new ArgumentNullException(nameof(error));
        }

        public string ProviderId { get; }
        public string Error { get; }
    }

    public sealed class WatchFrame
    {
        private readonly ReadOnlyDictionary<string, WatchFrameEntry>
            entries;
        private readonly ReadOnlyCollection<WatchProviderFailure>
            failures;

        internal WatchFrame(
            long sequence,
            TickStamp stamp,
            long movieTick,
            IDictionary<string, WatchFrameEntry> entries,
            IList<WatchProviderFailure> failures)
        {
            if (sequence <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence));
            }

            Sequence = sequence;
            Stamp = stamp;
            MovieTick = movieTick;
            this.entries =
                new ReadOnlyDictionary<string, WatchFrameEntry>(
                    new SortedDictionary<string, WatchFrameEntry>(
                        entries
                        ?? throw new ArgumentNullException(nameof(entries)),
                        StringComparer.Ordinal));
            this.failures =
                new ReadOnlyCollection<WatchProviderFailure>(
                    new List<WatchProviderFailure>(
                        failures
                        ?? throw new ArgumentNullException(
                            nameof(failures))));
        }

        public long Sequence { get; }
        public TickStamp Stamp { get; }
        public long MovieTick { get; }
        public IReadOnlyDictionary<string, WatchFrameEntry> Entries =>
            entries;
        public IReadOnlyList<WatchProviderFailure> Failures => failures;

        public bool TryGet(
            string key,
            out WatchFrameEntry entry)
        {
            return entries.TryGetValue(key, out entry!);
        }
    }
}
