using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Serialization;
using HollowKnightTAS.Core.State;

namespace HollowKnightTAS.Core.Verification
{
    public readonly struct VanillaTimelineSample
    {
        internal VanillaTimelineSample(
            int fixedSteps,
            float relativeTime,
            float unscaledRelativeTime,
            float fixedRelativeTime)
        {
            FixedSteps = fixedSteps;
            RelativeTime = relativeTime;
            UnscaledRelativeTime = unscaledRelativeTime;
            FixedRelativeTime = fixedRelativeTime;
        }

        public int FixedSteps { get; }
        public float RelativeTime { get; }
        public float UnscaledRelativeTime { get; }
        public float FixedRelativeTime { get; }
    }

    /// <summary>
    /// Builds a process-origin-independent logical timeline from the delta
    /// values and fixed-step counts observed at each sampled gameplay tick.
    /// It never reads or writes the game clock.
    /// </summary>
    public sealed class VanillaTimelineAccumulator
    {
        private bool started;
        private long previousFixedTick;
        private float relativeTime;
        private float unscaledRelativeTime;
        private float fixedRelativeTime;

        public VanillaTimelineSample Advance(
            long fixedTick,
            float deltaTime,
            float unscaledDeltaTime,
            float fixedDeltaTime)
        {
            if (fixedTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedTick));
            }
            RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            RequireFiniteNonNegative(
                unscaledDeltaTime,
                nameof(unscaledDeltaTime));
            RequireFiniteNonNegative(
                fixedDeltaTime,
                nameof(fixedDeltaTime));

            if (!started)
            {
                started = true;
                previousFixedTick = fixedTick;
                return new VanillaTimelineSample(0, 0f, 0f, 0f);
            }

            var fixedStepCount = fixedTick - previousFixedTick;
            if (fixedStepCount < 0 || fixedStepCount > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "The observed fixed tick must advance monotonically "
                    + "within Int32 range.");
            }

            var fixedSteps = checked((int)fixedStepCount);
            relativeTime += deltaTime;
            unscaledRelativeTime += unscaledDeltaTime;
            fixedRelativeTime += fixedSteps * fixedDeltaTime;
            previousFixedTick = fixedTick;
            return new VanillaTimelineSample(
                fixedSteps,
                relativeTime,
                unscaledRelativeTime,
                fixedRelativeTime);
        }

        private static void RequireFiniteNonNegative(
            float value,
            string parameterName)
        {
            if (float.IsNaN(value)
                || float.IsInfinity(value)
                || value < 0f)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }

    public sealed class VanillaEquivalenceField
    {
        internal VanillaEquivalenceField(
            string key,
            SemanticValue value,
            bool comparable)
        {
            Key = key;
            Value = value;
            Comparable = comparable;
        }

        public string Key { get; }
        public SemanticValue Value { get; }
        public bool Comparable { get; }
    }

    public sealed class VanillaEquivalenceFrame
    {
        internal VanillaEquivalenceFrame(
            long sequence,
            long logicalTick,
            IDictionary<string, VanillaEquivalenceField> fields)
        {
            if (sequence <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence));
            }
            if (logicalTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(logicalTick));
            }

            Sequence = sequence;
            LogicalTick = logicalTick;
            Fields = new ReadOnlyDictionary<string, VanillaEquivalenceField>(
                new SortedDictionary<string, VanillaEquivalenceField>(
                    fields ?? throw new ArgumentNullException(nameof(fields)),
                    StringComparer.Ordinal));
        }

        public const int SchemaVersion = 1;
        public long Sequence { get; }
        public long LogicalTick { get; }
        public IReadOnlyDictionary<string, VanillaEquivalenceField> Fields
        {
            get;
        }
    }

    public sealed class VanillaEquivalenceFrameBuilder
    {
        private readonly Dictionary<string, VanillaEquivalenceField> fields =
            new Dictionary<string, VanillaEquivalenceField>(
                StringComparer.Ordinal);

        public void AddBoolean(
            string key,
            bool value,
            bool comparable = true)
        {
            Add(key, SemanticValue.FromBoolean(value), comparable);
        }

        public void AddInt32(
            string key,
            int value,
            bool comparable = true)
        {
            Add(key, SemanticValue.FromInt32(value), comparable);
        }

        public void AddInt64(
            string key,
            long value,
            bool comparable = true)
        {
            Add(key, SemanticValue.FromInt64(value), comparable);
        }

        public void AddFloat32(
            string key,
            float value,
            bool comparable = true)
        {
            Add(key, SemanticValue.FromFloat32(value), comparable);
        }

        public void AddString(
            string key,
            string value,
            bool comparable = true)
        {
            Add(key, SemanticValue.FromString(value), comparable);
        }

        public VanillaEquivalenceFrame Build(
            long sequence,
            long logicalTick)
        {
            if (fields.Count == 0)
            {
                throw new InvalidOperationException(
                    "A vanilla-equivalence frame cannot be empty.");
            }

            return new VanillaEquivalenceFrame(
                sequence,
                logicalTick,
                fields);
        }

        private void Add(
            string key,
            SemanticValue value,
            bool comparable)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException(
                    "A non-empty field key is required.",
                    nameof(key));
            }
            if (fields.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    "Duplicate vanilla-equivalence field: " + key);
            }

            fields.Add(
                key,
                new VanillaEquivalenceField(key, value, comparable));
        }
    }

    public static class VanillaEquivalenceFrameJson
    {
        public static string Serialize(VanillaEquivalenceFrame frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            var builder = new StringBuilder(4096);
            builder.Append("{\"schemaVersion\":");
            builder.Append(VanillaEquivalenceFrame.SchemaVersion);
            builder.Append(",\"sequence\":");
            builder.Append(
                frame.Sequence.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"logicalTick\":");
            builder.Append(
                frame.LogicalTick.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"comparisonSha256\":");
            CanonicalJsonWriter.AppendString(
                builder,
                ComputeComparisonSha256(frame));
            builder.Append(",\"fields\":[");
            var index = 0;
            foreach (var field in frame.Fields.Values)
            {
                if (index++ > 0)
                {
                    builder.Append(',');
                }
                builder.Append("{\"key\":");
                CanonicalJsonWriter.AppendString(builder, field.Key);
                builder.Append(",\"kind\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    field.Value.Kind.ToString());
                builder.Append(",\"canonicalHex\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    field.Value.CanonicalHex);
                builder.Append(",\"displayValue\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    field.Value.DisplayValue);
                builder.Append(",\"comparable\":");
                builder.Append(field.Comparable ? "true" : "false");
                builder.Append('}');
            }
            builder.Append("]}");
            return builder.ToString();
        }

        public static string ComputeComparisonSha256(
            VanillaEquivalenceFrame frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            var builder = new StringBuilder(2048);
            builder.Append("{\"logicalTick\":");
            builder.Append(
                frame.LogicalTick.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"fields\":[");
            var index = 0;
            foreach (var field in frame.Fields.Values.Where(
                         value => value.Comparable))
            {
                if (index++ > 0)
                {
                    builder.Append(',');
                }
                builder.Append("{\"key\":");
                CanonicalJsonWriter.AppendString(builder, field.Key);
                builder.Append(",\"kind\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    field.Value.Kind.ToString());
                builder.Append(",\"canonicalHex\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    field.Value.CanonicalHex);
                builder.Append('}');
            }
            builder.Append("]}");
            return Sha256Utility.ComputeUtf8Hex(builder.ToString());
        }
    }

    public sealed class VanillaEquivalenceDifference
    {
        internal VanillaEquivalenceDifference(
            long logicalTick,
            string key,
            string expected,
            string actual)
        {
            LogicalTick = logicalTick;
            Key = key;
            Expected = expected;
            Actual = actual;
        }

        public long LogicalTick { get; }
        public string Key { get; }
        public string Expected { get; }
        public string Actual { get; }
    }

    public static class VanillaEquivalenceTraceComparer
    {
        public static VanillaEquivalenceDifference? FirstDifference(
            IReadOnlyList<VanillaEquivalenceFrame> expected,
            IReadOnlyList<VanillaEquivalenceFrame> actual)
        {
            if (expected == null)
            {
                throw new ArgumentNullException(nameof(expected));
            }
            if (actual == null)
            {
                throw new ArgumentNullException(nameof(actual));
            }

            var count = Math.Min(expected.Count, actual.Count);
            for (var index = 0; index < count; index++)
            {
                var left = expected[index];
                var right = actual[index];
                if (left.LogicalTick != right.LogicalTick)
                {
                    return new VanillaEquivalenceDifference(
                        Math.Min(left.LogicalTick, right.LogicalTick),
                        "$logicalTick",
                        left.LogicalTick.ToString(
                            CultureInfo.InvariantCulture),
                        right.LogicalTick.ToString(
                            CultureInfo.InvariantCulture));
                }

                var keys = left.Fields.Values
                    .Where(field => field.Comparable)
                    .Select(field => field.Key)
                    .Concat(
                        right.Fields.Values
                            .Where(field => field.Comparable)
                            .Select(field => field.Key))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal);
                foreach (var key in keys)
                {
                    if (!left.Fields.TryGetValue(key, out var leftField)
                        || !leftField.Comparable)
                    {
                        return Missing(left.LogicalTick, key, false);
                    }
                    if (!right.Fields.TryGetValue(key, out var rightField)
                        || !rightField.Comparable)
                    {
                        return Missing(left.LogicalTick, key, true);
                    }
                    if (!leftField.Value.Equals(rightField.Value))
                    {
                        return new VanillaEquivalenceDifference(
                            left.LogicalTick,
                            key,
                            leftField.Value.CanonicalHex,
                            rightField.Value.CanonicalHex);
                    }
                }
            }

            if (expected.Count != actual.Count)
            {
                return new VanillaEquivalenceDifference(
                    count,
                    "$frameCount",
                    expected.Count.ToString(CultureInfo.InvariantCulture),
                    actual.Count.ToString(CultureInfo.InvariantCulture));
            }

            return null;
        }

        private static VanillaEquivalenceDifference Missing(
            long logicalTick,
            string key,
            bool actualMissing)
        {
            return new VanillaEquivalenceDifference(
                logicalTick,
                key,
                actualMissing ? "present" : "<missing>",
                actualMissing ? "<missing>" : "present");
        }
    }
}
