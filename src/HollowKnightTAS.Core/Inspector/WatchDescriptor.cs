using System;
using HollowKnightTAS.Core.State;

namespace HollowKnightTAS.Core.Inspector
{
    public sealed class WatchDescriptor
    {
        public WatchDescriptor(
            WatchKey key,
            SemanticValueKind valueKind,
            string group,
            int sampleEveryMovieTicks,
            string label)
        {
            if (string.IsNullOrWhiteSpace(group))
            {
                throw new ArgumentException(
                    "A non-empty watch group is required.",
                    nameof(group));
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                throw new ArgumentException(
                    "A non-empty watch label is required.",
                    nameof(label));
            }

            if (sampleEveryMovieTicks <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampleEveryMovieTicks));
            }

            Key = key;
            ValueKind = valueKind;
            Group = group;
            SampleEveryMovieTicks = sampleEveryMovieTicks;
            Label = label;
        }

        public WatchKey Key { get; }
        public SemanticValueKind ValueKind { get; }
        public string Group { get; }
        public int SampleEveryMovieTicks { get; }
        public string Label { get; }
    }
}
