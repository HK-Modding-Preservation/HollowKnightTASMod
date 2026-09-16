using System;
using System.Globalization;
using System.Text;
using HollowKnightTAS.Core.Serialization;

namespace HollowKnightTAS.Core.State
{
    public static class SemanticSnapshotJson
    {
        public static string Serialize(
            SemanticSnapshot snapshot,
            string sha256)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (!Movie.MovieProtocolV1.IsLowerSha256(sha256))
            {
                throw new ArgumentException(
                    "A canonical snapshot SHA-256 is required.",
                    nameof(sha256));
            }

            var builder = new StringBuilder(2048);
            builder.Append("{\"schemaVersion\":");
            builder.Append(
                snapshot.SchemaVersion.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"sha256\":");
            CanonicalJsonWriter.AppendString(builder, sha256);
            builder.Append(",\"values\":[");
            var index = 0;
            foreach (var pair in snapshot.Values)
            {
                if (index++ > 0)
                {
                    builder.Append(',');
                }

                builder.Append("{\"key\":");
                CanonicalJsonWriter.AppendString(builder, pair.Key);
                builder.Append(",\"kind\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    pair.Value.Kind.ToString());
                builder.Append(",\"canonicalHex\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    pair.Value.CanonicalHex);
                builder.Append(",\"displayValue\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    pair.Value.DisplayValue);
                builder.Append('}');
            }

            builder.Append("]}");
            return builder.ToString();
        }
    }
}
