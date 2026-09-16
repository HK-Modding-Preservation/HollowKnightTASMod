using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace HollowKnightTAS.Core.State
{
    public sealed class SemanticDiffEntry
    {
        internal SemanticDiffEntry(
            string key,
            SemanticValue? expected,
            SemanticValue? actual)
        {
            Key = key;
            ExpectedKind = expected?.Kind;
            ActualKind = actual?.Kind;
            ExpectedBits = expected?.CanonicalHex ?? "<missing>";
            ActualBits = actual?.CanonicalHex ?? "<missing>";
            ExpectedDisplay = expected?.DisplayValue ?? "<missing>";
            ActualDisplay = actual?.DisplayValue ?? "<missing>";
        }

        public string Key { get; }
        public SemanticValueKind? ExpectedKind { get; }
        public SemanticValueKind? ActualKind { get; }
        public string ExpectedBits { get; }
        public string ActualBits { get; }
        public string ExpectedDisplay { get; }
        public string ActualDisplay { get; }
    }

    public sealed class SemanticDiff
    {
        internal SemanticDiff(IList<SemanticDiffEntry> entries)
        {
            Entries = new ReadOnlyCollection<SemanticDiffEntry>(entries);
        }

        public bool AreEqual => Entries.Count == 0;
        public IReadOnlyList<SemanticDiffEntry> Entries { get; }
        public SemanticDiffEntry? FirstDifference =>
            Entries.Count == 0 ? null : Entries[0];
    }

    public static class SemanticSnapshotDiffer
    {
        public static SemanticDiff Compare(
            SemanticSnapshot expected,
            SemanticSnapshot actual)
        {
            if (expected == null)
            {
                throw new ArgumentNullException(nameof(expected));
            }

            if (actual == null)
            {
                throw new ArgumentNullException(nameof(actual));
            }

            var keys = expected.Values.Keys
                .Concat(actual.Values.Keys)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal);
            var differences = new List<SemanticDiffEntry>();
            foreach (var key in keys)
            {
                var hasExpected = expected.Values.TryGetValue(key, out var expectedValue);
                var hasActual = actual.Values.TryGetValue(key, out var actualValue);
                if (!hasExpected
                    || !hasActual
                    || !expectedValue.Equals(actualValue))
                {
                    differences.Add(
                        new SemanticDiffEntry(
                            key,
                            hasExpected ? expectedValue : (SemanticValue?)null,
                            hasActual ? actualValue : (SemanticValue?)null));
                }
            }

            return new SemanticDiff(differences);
        }
    }
}
