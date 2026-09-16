using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using HollowKnightTAS.Core.ReplaySave;
using HollowKnightTAS.Core.Serialization;

namespace HollowKnightTAS.Core.Keyframes
{
    public static class SemanticKeyframeDescriptorCodec
    {
        public const int MaximumBytes = 256 * 1024;

        public static byte[] Serialize(
            SemanticKeyframeDescriptor value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            var builder = new StringBuilder(4096);
            builder.Append('{');
            ReplaySaveJson.AppendString(
                builder,
                "adapterManifestSha256",
                value.AdapterManifestSha256);
            AppendAdapters(builder, value.Adapters);
            ReplaySaveJson.AppendString(
                builder,
                "baselineObjectSha256",
                value.BaselineObjectSha256);
            ReplaySaveJson.AppendInt64(
                builder,
                "captureMovieTick",
                value.CaptureMovieTick);
            ReplaySaveJson.AppendString(
                builder,
                "entryGateId",
                value.EntryGateId);
            ReplaySaveJson.AppendString(
                builder,
                "gameBuildSha256",
                value.GameBuildSha256);
            ReplaySaveJson.AppendString(
                builder,
                "journalHeadSha256",
                value.JournalHeadSha256);
            ReplaySaveJson.AppendString(
                builder,
                "keyframeId",
                value.KeyframeId);
            ReplaySaveJson.AppendString(
                builder,
                "manifestSha256",
                value.ManifestSha256);
            ReplaySaveJson.AppendString(
                builder,
                "rngStateSha256",
                value.RngStateSha256);
            ReplaySaveJson.AppendInt32(
                builder,
                "sceneEpoch",
                value.SceneEpoch);
            ReplaySaveJson.AppendString(
                builder,
                "sceneName",
                value.SceneName);
            ReplaySaveJson.AppendInt32(
                builder,
                "schemaVersion",
                value.SchemaVersion);
            ReplaySaveJson.AppendString(
                builder,
                "semanticSnapshotSha256",
                value.SemanticSnapshotSha256);
            ReplaySaveJson.AppendString(
                builder,
                "status",
                value.Status.ToString());
            ReplaySaveJson.AppendString(
                builder,
                "tier",
                value.Tier.ToString());
            builder.Append('}');
            return ToBoundedBytes(builder, MaximumBytes);
        }

        public static SemanticKeyframeDescriptor Deserialize(
            byte[] bytes)
        {
            var data =
                ReplaySaveJson.Deserialize<
                    SemanticKeyframeDescriptorData>(
                    bytes,
                    MaximumBytes);
            try
            {
                if (!Enum.TryParse(
                        data.Tier,
                        false,
                        out KeyframeSupportTier tier)
                    || !Enum.TryParse(
                        data.Status,
                        false,
                        out KeyframeStatus status))
                {
                    throw new InvalidDataException(
                        "Keyframe enum is invalid.");
                }
                var value = new SemanticKeyframeDescriptor(
                    data.SchemaVersion,
                    data.KeyframeId ?? string.Empty,
                    tier,
                    status,
                    data.CaptureMovieTick,
                    data.SceneName ?? string.Empty,
                    data.EntryGateId ?? string.Empty,
                    data.SceneEpoch,
                    data.GameBuildSha256 ?? string.Empty,
                    data.ManifestSha256 ?? string.Empty,
                    data.BaselineObjectSha256 ?? string.Empty,
                    data.JournalHeadSha256 ?? string.Empty,
                    data.AdapterManifestSha256 ?? string.Empty,
                    data.SemanticSnapshotSha256 ?? string.Empty,
                    data.RngStateSha256 ?? string.Empty,
                    (data.Adapters
                     ?? Array.Empty<KeyframeAdapterEnvelopeData>())
                    .Select(
                        item => new KeyframeAdapterEnvelope(
                            item.AdapterId ?? string.Empty,
                            item.SchemaVersion,
                            item.IsRequired,
                            item.PayloadSha256
                            ?? string.Empty,
                            item.PayloadLength)));
                RequireCanonical(bytes, Serialize(value));
                return value;
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is InvalidDataException)
            {
                throw new InvalidDataException(
                    "Semantic keyframe descriptor is invalid.",
                    exception);
            }
        }

        private static void AppendAdapters(
            StringBuilder builder,
            IReadOnlyList<KeyframeAdapterEnvelope> adapters)
        {
            AppendArrayPrefix(builder, "adapters");
            for (var index = 0;
                 index < adapters.Count;
                 index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }
                var value = adapters[index];
                builder.Append('{');
                ReplaySaveJson.AppendString(
                    builder,
                    "adapterId",
                    value.AdapterId);
                ReplaySaveJson.AppendBoolean(
                    builder,
                    "isRequired",
                    value.IsRequired);
                ReplaySaveJson.AppendInt32(
                    builder,
                    "payloadLength",
                    value.PayloadLength);
                ReplaySaveJson.AppendString(
                    builder,
                    "payloadSha256",
                    value.PayloadSha256);
                ReplaySaveJson.AppendInt32(
                    builder,
                    "schemaVersion",
                    value.SchemaVersion);
                builder.Append('}');
            }
            builder.Append(']');
        }

        internal static void AppendArrayPrefix(
            StringBuilder builder,
            string name)
        {
            if (builder[builder.Length - 1] != '{')
            {
                builder.Append(',');
            }
            CanonicalJsonWriter.AppendString(builder, name);
            builder.Append(":[");
        }

        internal static byte[] ToBoundedBytes(
            StringBuilder builder,
            int maximumBytes)
        {
            var bytes =
                ReplaySaveJson.StrictUtf8.GetBytes(
                    builder.ToString());
            if (bytes.Length == 0 || bytes.Length > maximumBytes)
            {
                throw new InvalidDataException(
                    "Canonical keyframe JSON exceeds its size limit.");
            }
            return bytes;
        }

        internal static void RequireCanonical(
            byte[] actual,
            byte[] canonical)
        {
            if (actual == null
                || actual.Length != canonical.Length)
            {
                throw new InvalidDataException(
                    "Keyframe JSON is not canonical.");
            }
            var difference = 0;
            for (var index = 0;
                 index < actual.Length;
                 index++)
            {
                difference |= actual[index] ^ canonical[index];
            }
            if (difference != 0)
            {
                throw new InvalidDataException(
                    "Keyframe JSON is not canonical.");
            }
        }
    }

    public static class KeyframeAdapterManifestCodec
    {
        public const int MaximumBytes = 128 * 1024;

        public static byte[] Serialize(
            KeyframeAdapterManifest value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            var builder = new StringBuilder(2048);
            builder.Append('{');
            SemanticKeyframeDescriptorCodec.AppendArrayPrefix(
                builder,
                "adapters");
            for (var index = 0;
                 index < value.Adapters.Count;
                 index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }
                var item = value.Adapters[index];
                builder.Append('{');
                ReplaySaveJson.AppendString(
                    builder,
                    "adapterId",
                    item.AdapterId);
                ReplaySaveJson.AppendString(
                    builder,
                    "buildProfileId",
                    item.BuildProfileId);
                ReplaySaveJson.AppendBoolean(
                    builder,
                    "isRequired",
                    item.IsRequired);
                ReplaySaveJson.AppendString(
                    builder,
                    "minimumTier",
                    item.MinimumTier.ToString());
                ReplaySaveJson.AppendInt32(
                    builder,
                    "schemaVersion",
                    item.SchemaVersion);
                builder.Append('}');
            }
            builder.Append(']');
            ReplaySaveJson.AppendInt32(
                builder,
                "schemaVersion",
                value.SchemaVersion);
            builder.Append('}');
            return SemanticKeyframeDescriptorCodec
                .ToBoundedBytes(builder, MaximumBytes);
        }

        public static KeyframeAdapterManifest Deserialize(
            byte[] bytes)
        {
            var data =
                ReplaySaveJson.Deserialize<
                    KeyframeAdapterManifestData>(
                    bytes,
                    MaximumBytes);
            try
            {
                var value = new KeyframeAdapterManifest(
                    data.SchemaVersion,
                    (data.Adapters
                     ?? Array.Empty<
                         KeyframeAdapterManifestEntryData>())
                    .Select(
                        item =>
                        {
                            if (!Enum.TryParse(
                                    item.MinimumTier,
                                    false,
                                    out KeyframeSupportTier tier))
                            {
                                throw new InvalidDataException(
                                    "Adapter tier is invalid.");
                            }
                            return new KeyframeAdapterManifestEntry(
                                item.AdapterId ?? string.Empty,
                                item.SchemaVersion,
                                item.IsRequired,
                                tier,
                                item.BuildProfileId
                                ?? string.Empty);
                        }));
                SemanticKeyframeDescriptorCodec
                    .RequireCanonical(bytes, Serialize(value));
                return value;
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is InvalidDataException)
            {
                throw new InvalidDataException(
                    "Keyframe adapter manifest is invalid.",
                    exception);
            }
        }
    }

    public static class KeyframeArtifactIndexCodec
    {
        public const int MaximumBytes = 4 * 1024 * 1024;

        public static byte[] Serialize(
            KeyframeArtifactIndex value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            var builder = new StringBuilder(
                Math.Min(
                    MaximumBytes,
                    128 + value.Entries.Count * 160));
            builder.Append('{');
            SemanticKeyframeDescriptorCodec.AppendArrayPrefix(
                builder,
                "entries");
            for (var index = 0;
                 index < value.Entries.Count;
                 index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }
                var item = value.Entries[index];
                builder.Append('{');
                ReplaySaveJson.AppendString(
                    builder,
                    "keyframeId",
                    item.KeyframeId);
                ReplaySaveJson.AppendInt64(
                    builder,
                    "keyframeMovieTick",
                    item.KeyframeMovieTick);
                ReplaySaveJson.AppendString(
                    builder,
                    "replaySaveId",
                    item.ReplaySaveId);
                ReplaySaveJson.AppendInt64(
                    builder,
                    "targetMovieTick",
                    item.TargetMovieTick);
                builder.Append('}');
            }
            builder.Append(']');
            ReplaySaveJson.AppendInt32(
                builder,
                "schemaVersion",
                value.SchemaVersion);
            builder.Append('}');
            return SemanticKeyframeDescriptorCodec
                .ToBoundedBytes(builder, MaximumBytes);
        }

        public static KeyframeArtifactIndex Deserialize(
            byte[] bytes)
        {
            var data =
                ReplaySaveJson.Deserialize<
                    KeyframeArtifactIndexData>(
                    bytes,
                    MaximumBytes);
            try
            {
                var value = new KeyframeArtifactIndex(
                    data.SchemaVersion,
                    (data.Entries
                     ?? Array.Empty<KeyframeIndexEntryData>())
                    .Select(
                        item => new KeyframeIndexEntry(
                            item.ReplaySaveId
                            ?? string.Empty,
                            item.KeyframeId
                            ?? string.Empty,
                            item.KeyframeMovieTick,
                            item.TargetMovieTick)));
                SemanticKeyframeDescriptorCodec
                    .RequireCanonical(bytes, Serialize(value));
                return value;
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is InvalidDataException)
            {
                throw new InvalidDataException(
                    "Keyframe artifact index is invalid.",
                    exception);
            }
        }
    }

    [DataContract]
    internal sealed class SemanticKeyframeDescriptorData
    {
        [DataMember(Name = "adapterManifestSha256")]
        public string? AdapterManifestSha256 { get; set; }

        [DataMember(Name = "adapters")]
        public KeyframeAdapterEnvelopeData[]? Adapters { get; set; }

        [DataMember(Name = "baselineObjectSha256")]
        public string? BaselineObjectSha256 { get; set; }

        [DataMember(Name = "captureMovieTick")]
        public long CaptureMovieTick { get; set; }

        [DataMember(Name = "entryGateId")]
        public string? EntryGateId { get; set; }

        [DataMember(Name = "gameBuildSha256")]
        public string? GameBuildSha256 { get; set; }

        [DataMember(Name = "journalHeadSha256")]
        public string? JournalHeadSha256 { get; set; }

        [DataMember(Name = "keyframeId")]
        public string? KeyframeId { get; set; }

        [DataMember(Name = "manifestSha256")]
        public string? ManifestSha256 { get; set; }

        [DataMember(Name = "rngStateSha256")]
        public string? RngStateSha256 { get; set; }

        [DataMember(Name = "sceneEpoch")]
        public int SceneEpoch { get; set; }

        [DataMember(Name = "sceneName")]
        public string? SceneName { get; set; }

        [DataMember(Name = "schemaVersion")]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "semanticSnapshotSha256")]
        public string? SemanticSnapshotSha256 { get; set; }

        [DataMember(Name = "status")]
        public string? Status { get; set; }

        [DataMember(Name = "tier")]
        public string? Tier { get; set; }
    }

    [DataContract]
    internal sealed class KeyframeAdapterEnvelopeData
    {
        [DataMember(Name = "adapterId")]
        public string? AdapterId { get; set; }

        [DataMember(Name = "isRequired")]
        public bool IsRequired { get; set; }

        [DataMember(Name = "payloadLength")]
        public int PayloadLength { get; set; }

        [DataMember(Name = "payloadSha256")]
        public string? PayloadSha256 { get; set; }

        [DataMember(Name = "schemaVersion")]
        public int SchemaVersion { get; set; }
    }

    [DataContract]
    internal sealed class KeyframeAdapterManifestData
    {
        [DataMember(Name = "adapters")]
        public KeyframeAdapterManifestEntryData[]? Adapters { get; set; }

        [DataMember(Name = "schemaVersion")]
        public int SchemaVersion { get; set; }
    }

    [DataContract]
    internal sealed class KeyframeAdapterManifestEntryData
    {
        [DataMember(Name = "adapterId")]
        public string? AdapterId { get; set; }

        [DataMember(Name = "buildProfileId")]
        public string? BuildProfileId { get; set; }

        [DataMember(Name = "isRequired")]
        public bool IsRequired { get; set; }

        [DataMember(Name = "minimumTier")]
        public string? MinimumTier { get; set; }

        [DataMember(Name = "schemaVersion")]
        public int SchemaVersion { get; set; }
    }

    [DataContract]
    internal sealed class KeyframeArtifactIndexData
    {
        [DataMember(Name = "entries")]
        public KeyframeIndexEntryData[]? Entries { get; set; }

        [DataMember(Name = "schemaVersion")]
        public int SchemaVersion { get; set; }
    }

    [DataContract]
    internal sealed class KeyframeIndexEntryData
    {
        [DataMember(Name = "keyframeId")]
        public string? KeyframeId { get; set; }

        [DataMember(Name = "keyframeMovieTick")]
        public long KeyframeMovieTick { get; set; }

        [DataMember(Name = "replaySaveId")]
        public string? ReplaySaveId { get; set; }

        [DataMember(Name = "targetMovieTick")]
        public long TargetMovieTick { get; set; }
    }
}
