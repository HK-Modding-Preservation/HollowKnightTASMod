using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using HollowKnightTAS.Core.Serialization;

namespace HollowKnightTAS.Core.ReplaySave
{
    internal static class ReplaySaveJson
    {
        internal static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(false, true);

        public static T Deserialize<T>(byte[] bytes, int maximumBytes)
            where T : class
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            if (bytes.Length == 0 || bytes.Length > maximumBytes)
            {
                throw new InvalidDataException(
                    "JSON object size is outside the allowed range.");
            }

            try
            {
                using (var stream = new MemoryStream(bytes, writable: false))
                {
                    var serializer = new DataContractJsonSerializer(typeof(T));
                    var value = serializer.ReadObject(stream) as T;
                    if (value == null || stream.Position != stream.Length)
                    {
                        throw new InvalidDataException(
                            "JSON object is empty or has trailing data.");
                    }

                    return value;
                }
            }
            catch (Exception exception) when (
                exception is SerializationException
                || exception is DecoderFallbackException
                || exception is XmlException)
            {
                throw new InvalidDataException(
                    "JSON object is malformed.",
                    exception);
            }
        }

        public static void AppendString(
            StringBuilder builder,
            string name,
            string value)
        {
            AppendPrefix(builder, name);
            CanonicalJsonWriter.AppendString(builder, value ?? string.Empty);
        }

        public static void AppendInt32(
            StringBuilder builder,
            string name,
            int value)
        {
            AppendPrefix(builder, name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        public static void AppendInt64(
            StringBuilder builder,
            string name,
            long value)
        {
            AppendPrefix(builder, name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        public static void AppendBoolean(
            StringBuilder builder,
            string name,
            bool value)
        {
            AppendPrefix(builder, name);
            builder.Append(value ? "true" : "false");
        }

        public static string FormatUtc(DateTimeOffset value)
        {
            return value
                .ToUniversalTime()
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        }

        public static DateTimeOffset ParseUtc(string? value, string name)
        {
            if (!DateTimeOffset.TryParseExact(
                    value,
                    "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal
                    | DateTimeStyles.AdjustToUniversal,
                    out var result))
            {
                throw new InvalidDataException(name + " is not canonical UTC time.");
            }

            return result;
        }

        private static void AppendPrefix(StringBuilder builder, string name)
        {
            if (builder.Length == 0)
            {
                throw new InvalidOperationException(
                    "JSON builder must already contain an object or array opener.");
            }

            var previous = builder[builder.Length - 1];
            if (previous != '{' && previous != '[')
            {
                builder.Append(',');
            }

            CanonicalJsonWriter.AppendString(builder, name);
            builder.Append(':');
        }
    }

    [DataContract]
    internal sealed class ReplaySaveDescriptorData
    {
        [DataMember(Name = "schemaVersion")]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "replaySaveId")]
        public string? ReplaySaveId { get; set; }

        [DataMember(Name = "label")]
        public string? Label { get; set; }

        [DataMember(Name = "reason")]
        public string? Reason { get; set; }

        [DataMember(Name = "requestedAtUtc")]
        public string? RequestedAtUtc { get; set; }

        [DataMember(Name = "createdAtUtc")]
        public string? CreatedAtUtc { get; set; }

        [DataMember(Name = "requestedAtMovieTick")]
        public long RequestedAtMovieTick { get; set; }

        [DataMember(Name = "effectiveMovieTick")]
        public long EffectiveMovieTick { get; set; }

        [DataMember(Name = "manifestSha256")]
        public string? ManifestSha256 { get; set; }

        [DataMember(Name = "baselineObjectSha256")]
        public string? BaselineObjectSha256 { get; set; }

        [DataMember(Name = "baselineId")]
        public string? BaselineId { get; set; }

        [DataMember(Name = "baselineSemanticSha256")]
        public string? BaselineSemanticSha256 { get; set; }

        [DataMember(Name = "movieObjectSha256")]
        public string? MovieObjectSha256 { get; set; }

        [DataMember(Name = "journalHeadSha256")]
        public string? JournalHeadSha256 { get; set; }

        [DataMember(Name = "journalSegmentObjectSha256s")]
        public string[]? JournalSegmentObjectSha256s { get; set; }

        [DataMember(Name = "semanticSnapshotSha256")]
        public string? SemanticSnapshotSha256 { get; set; }

        [DataMember(Name = "ledgerSummarySha256")]
        public string? LedgerSummarySha256 { get; set; }

        [DataMember(Name = "sceneName")]
        public string? SceneName { get; set; }

        [DataMember(Name = "sceneEpoch")]
        public int SceneEpoch { get; set; }

        [DataMember(Name = "autoRetentionCount")]
        public int AutoRetentionCount { get; set; }

        [DataMember(Name = "lifecycleObjectSha256", EmitDefaultValue = false)]
        public string? LifecycleObjectSha256 { get; set; }
    }
}
