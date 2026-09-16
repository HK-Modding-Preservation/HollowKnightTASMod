using System;
using System.Globalization;
using System.Text;
using HollowKnightTAS.Core.Serialization;

namespace HollowKnightTAS.Core.Inspector
{
    public static class WatchFrameJson
    {
        public const int SchemaVersion = 1;

        public static string Serialize(WatchFrame frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            var builder = new StringBuilder(4096);
            builder.Append("{\"schemaVersion\":");
            builder.Append(
                SchemaVersion.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"sequence\":");
            builder.Append(
                frame.Sequence.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"movieTick\":");
            builder.Append(
                frame.MovieTick.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"stamp\":{");
            AppendNumber(builder, "inputTick", checked((long)frame.Stamp.InputTick));
            AppendNumber(builder, "visualTick", frame.Stamp.VisualTick);
            AppendNumber(builder, "fixedTick", frame.Stamp.FixedTick);
            AppendNumber(builder, "sceneEpoch", frame.Stamp.SceneEpoch);
            AppendString(builder, "phase", frame.Stamp.Phase.ToString());
            builder.Append("},\"entries\":[");
            var index = 0;
            foreach (var pair in frame.Entries)
            {
                if (index++ > 0)
                {
                    builder.Append(',');
                }

                var entry = pair.Value;
                builder.Append('{');
                AppendString(builder, "key", pair.Key);
                AppendBoolean(
                    builder,
                    "verificationStable",
                    entry.Descriptor.Key.IsVerificationStable);
                AppendString(
                    builder,
                    "kind",
                    entry.Descriptor.ValueKind.ToString());
                AppendString(
                    builder,
                    "group",
                    entry.Descriptor.Group);
                AppendString(
                    builder,
                    "label",
                    entry.Descriptor.Label);
                AppendString(
                    builder,
                    "canonicalHex",
                    entry.Value.CanonicalHex);
                AppendString(
                    builder,
                    "displayValue",
                    entry.Value.DisplayValue);
                AppendNumber(
                    builder,
                    "sampledAtMovieTick",
                    entry.SampledAtMovieTick);
                AppendNumber(
                    builder,
                    "ageMovieTicks",
                    entry.AgeMovieTicks);
                AppendBoolean(builder, "fresh", entry.IsFresh);
                builder.Append('}');
            }

            builder.Append("],\"failures\":[");
            for (var failureIndex = 0;
                 failureIndex < frame.Failures.Count;
                 failureIndex++)
            {
                if (failureIndex > 0)
                {
                    builder.Append(',');
                }

                var failure = frame.Failures[failureIndex];
                builder.Append('{');
                AppendString(
                    builder,
                    "providerId",
                    failure.ProviderId);
                AppendString(builder, "error", failure.Error);
                builder.Append('}');
            }

            builder.Append("]}");
            return builder.ToString();
        }

        private static void AppendString(
            StringBuilder builder,
            string name,
            string value)
        {
            AppendPrefix(builder, name);
            CanonicalJsonWriter.AppendString(builder, value);
        }

        private static void AppendNumber(
            StringBuilder builder,
            string name,
            long value)
        {
            AppendPrefix(builder, name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendBoolean(
            StringBuilder builder,
            string name,
            bool value)
        {
            AppendPrefix(builder, name);
            builder.Append(value ? "true" : "false");
        }

        private static void AppendPrefix(
            StringBuilder builder,
            string name)
        {
            if (builder[builder.Length - 1] != '{')
            {
                builder.Append(',');
            }

            CanonicalJsonWriter.AppendString(builder, name);
            builder.Append(':');
        }
    }
}
