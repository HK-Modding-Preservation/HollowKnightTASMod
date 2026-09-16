using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HollowKnightTAS.Core.State
{
    public sealed class SemanticSnapshot
    {
        private readonly ReadOnlyDictionary<string, SemanticValue> values;

        internal SemanticSnapshot(
            int schemaVersion,
            IDictionary<string, SemanticValue> source)
        {
            if (!SemanticSnapshotSchemas.IsSupported(schemaVersion))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(schemaVersion),
                    schemaVersion,
                    "Unsupported semantic snapshot schema.");
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            SchemaVersion = schemaVersion;
            values = new ReadOnlyDictionary<string, SemanticValue>(
                new SortedDictionary<string, SemanticValue>(
                    source,
                    StringComparer.Ordinal));
        }

        public int SchemaVersion { get; }
        public IReadOnlyDictionary<string, SemanticValue> Values => values;
    }
}
