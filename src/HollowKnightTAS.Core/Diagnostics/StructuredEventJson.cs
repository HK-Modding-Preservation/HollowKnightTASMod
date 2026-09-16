using System;
using System.Globalization;
using System.Linq;
using System.Text;
using HollowKnightTAS.Core.Serialization;

namespace HollowKnightTAS.Core.Diagnostics
{
    public static class StructuredEventJson
    {
        public static string Serialize(StructuredEvent value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var builder = new StringBuilder(512);
            builder.Append('{');
            builder.Append("\"schemaVersion\":");
            builder.Append(value.SchemaVersion.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"sessionId\":");
            CanonicalJsonWriter.AppendString(builder, value.SessionId);
            builder.Append(",\"sequence\":");
            builder.Append(value.Sequence.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"eventType\":");
            CanonicalJsonWriter.AppendString(builder, value.EventType);
            builder.Append(",\"timestampUtc\":");
            CanonicalJsonWriter.AppendString(
                builder,
                value.TimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            builder.Append(",\"fields\":{");

            var first = true;
            foreach (var item in value.Fields.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (!first)
                {
                    builder.Append(',');
                }

                CanonicalJsonWriter.AppendString(builder, item.Key);
                builder.Append(':');
                CanonicalJsonWriter.AppendString(builder, item.Value);
                first = false;
            }

            builder.Append("}}");
            return builder.ToString();
        }
    }
}

