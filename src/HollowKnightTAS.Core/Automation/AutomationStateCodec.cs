using System;
using System.Globalization;
using System.Linq;
using System.Text;
using HollowKnightTAS.Core.Serialization;

namespace HollowKnightTAS.Core.Automation
{
    public static class AutomationStateCodec
    {
        public static string Serialize(AutomationStateEnvelope value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var builder = new StringBuilder(8192);
            builder.Append("{\"schemaVersion\":");
            builder.Append(
                value.SchemaVersion.ToString(
                    CultureInfo.InvariantCulture));
            AppendString(builder, "sessionId", value.SessionId);
            AppendString(
                builder,
                "manifestSha256",
                value.ManifestSha256);
            AppendString(
                builder,
                "runtimeMode",
                value.RuntimeMode);
            builder.Append(",\"movieTick\":");
            builder.Append(
                value.MovieTick.ToString(
                    CultureInfo.InvariantCulture));
            AppendString(builder, "tickPhase", value.TickPhase);
            AppendString(
                builder,
                "capturedAtUtc",
                value.CapturedAtUtc.ToString(
                    "O",
                    CultureInfo.InvariantCulture));
            builder.Append(",\"ageMilliseconds\":");
            builder.Append(
                value.AgeMilliseconds.ToString(
                    CultureInfo.InvariantCulture));
            AppendString(
                builder,
                "semanticSnapshotSha256",
                value.SemanticSnapshotSha256);
            builder.Append(",\"fields\":{");
            var first = true;
            foreach (var pair in value.Fields.OrderBy(
                         item => item.Key,
                         StringComparer.Ordinal))
            {
                if (!first)
                {
                    builder.Append(',');
                }

                CanonicalJsonWriter.AppendString(builder, pair.Key);
                builder.Append(':');
                CanonicalJsonWriter.AppendString(builder, pair.Value);
                first = false;
            }

            builder.Append("},\"activeCapabilities\":[");
            first = true;
            foreach (var capability in value.ActiveCapabilities
                         .OrderBy(
                             item => item,
                             StringComparer.Ordinal))
            {
                if (!first)
                {
                    builder.Append(',');
                }

                CanonicalJsonWriter.AppendString(
                    builder,
                    capability);
                first = false;
            }

            builder.Append("]}");
            return builder.ToString();
        }

        private static void AppendString(
            StringBuilder builder,
            string name,
            string value)
        {
            builder.Append(',');
            CanonicalJsonWriter.AppendString(builder, name);
            builder.Append(':');
            CanonicalJsonWriter.AppendString(builder, value);
        }
    }
}
