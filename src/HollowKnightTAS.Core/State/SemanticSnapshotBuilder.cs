using System;
using System.Collections.Generic;
using System.Linq;

namespace HollowKnightTAS.Core.State
{
    public sealed class SemanticSnapshotBuilder
    {
        private readonly Dictionary<string, SemanticValue> values =
            new Dictionary<string, SemanticValue>(StringComparer.Ordinal);

        public SemanticSnapshotBuilder()
            : this(SemanticSnapshotSchemaV1.Version)
        {
        }

        public SemanticSnapshotBuilder(int schemaVersion)
        {
            if (!SemanticSnapshotSchemas.IsSupported(schemaVersion))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(schemaVersion),
                    schemaVersion,
                    "Unsupported semantic snapshot schema.");
            }

            SchemaVersion = schemaVersion;
        }

        public int SchemaVersion { get; }
        public int Count => values.Count;

        public void AddBoolean(string key, bool value)
        {
            Add(key, SemanticValue.FromBoolean(value));
        }

        public void AddInt32(string key, int value)
        {
            Add(key, SemanticValue.FromInt32(value));
        }

        public void AddInt64(string key, long value)
        {
            Add(key, SemanticValue.FromInt64(value));
        }

        public void AddFloat32(string key, float value)
        {
            Add(key, SemanticValue.FromFloat32(value));
        }

        public void AddFloat32Bits(string key, int bits)
        {
            Add(key, SemanticValue.FromFloat32Bits(bits));
        }

        public void AddString(string key, string value)
        {
            Add(key, SemanticValue.FromString(value));
        }

        public SemanticSnapshot Build()
        {
            var missing = SemanticSnapshotSchemas.Keys(SchemaVersion)
                .Where(key => !values.ContainsKey(key))
                .ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    "Semantic snapshot is missing required keys: "
                    + string.Join(", ", missing));
            }

            return new SemanticSnapshot(SchemaVersion, values);
        }

        internal void Add(string key, SemanticValue value)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException(
                    "A non-empty semantic key is required.",
                    nameof(key));
            }

            if (!SemanticSnapshotSchemas.TryGetKind(SchemaVersion, key, out var expectedKind))
            {
                throw new ArgumentException(
                    "Unknown semantic snapshot key for schema " + SchemaVersion + ": " + key,
                    nameof(key));
            }

            if (value.Kind != expectedKind)
            {
                throw new ArgumentException(
                    "Semantic key "
                    + key
                    + " requires "
                    + expectedKind
                    + " but received "
                    + value.Kind
                    + ".",
                    nameof(value));
            }

            if (values.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    "Duplicate semantic snapshot key: " + key);
            }

            values.Add(key, value);
        }
    }
}
