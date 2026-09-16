using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Core.Keyframes
{
    public enum KeyframeSupportTier : byte
    {
        ReplayOnly = 0,
        RoomEntry = 1,
        RegisteredRuntime = 2
    }

    public enum KeyframeStatus : byte
    {
        Ready = 1,
        Incompatible = 2,
        UnsupportedAdapter = 3,
        CaptureUnsafe = 4,
        RestoreDesync = 5,
        FallbackToReplay = 6
    }

    public sealed class KeyframeAdapterEnvelope
    {
        public const int MaximumPayloadBytes = 1024 * 1024;

        public KeyframeAdapterEnvelope(
            string adapterId,
            int schemaVersion,
            bool isRequired,
            string payloadSha256,
            int payloadLength)
        {
            AdapterId = RequireId(adapterId, nameof(adapterId));
            if (schemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(schemaVersion));
            }
            if (!MovieProtocolV1.IsLowerSha256(payloadSha256))
            {
                throw new ArgumentException(
                    "Adapter payload hash must be lowercase SHA-256.",
                    nameof(payloadSha256));
            }
            if (payloadLength <= 0
                || payloadLength > MaximumPayloadBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(payloadLength));
            }

            SchemaVersion = schemaVersion;
            IsRequired = isRequired;
            PayloadSha256 = payloadSha256;
            PayloadLength = payloadLength;
        }

        public string AdapterId { get; }
        public int SchemaVersion { get; }
        public bool IsRequired { get; }
        public string PayloadSha256 { get; }
        public int PayloadLength { get; }

        internal static string RequireId(
            string value,
            string name)
        {
            if (!MovieProtocolV1.IsIdentifier(value)
                || value.Length > 128)
            {
                throw new ArgumentException(
                    "A bounded canonical identifier is required.",
                    name);
            }
            return value;
        }
    }

    public sealed class SemanticKeyframeDescriptor
    {
        public const int CurrentSchemaVersion = 1;

        public SemanticKeyframeDescriptor(
            int schemaVersion,
            string keyframeId,
            KeyframeSupportTier tier,
            KeyframeStatus status,
            long captureMovieTick,
            string sceneName,
            string entryGateId,
            int sceneEpoch,
            string gameBuildSha256,
            string manifestSha256,
            string baselineObjectSha256,
            string journalHeadSha256,
            string adapterManifestSha256,
            string semanticSnapshotSha256,
            string rngStateSha256,
            IEnumerable<KeyframeAdapterEnvelope> adapters)
        {
            if (schemaVersion != CurrentSchemaVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(schemaVersion));
            }
            KeyframeId = KeyframeAdapterEnvelope.RequireId(
                keyframeId,
                nameof(keyframeId));
            if (!Enum.IsDefined(
                    typeof(KeyframeSupportTier),
                    tier)
                || tier == KeyframeSupportTier.ReplayOnly)
            {
                throw new ArgumentOutOfRangeException(nameof(tier));
            }
            if (!Enum.IsDefined(
                    typeof(KeyframeStatus),
                    status))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }
            if (captureMovieTick < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(captureMovieTick));
            }
            if (string.IsNullOrWhiteSpace(sceneName)
                || sceneName.Length > 256
                || sceneName.Any(character => character < 0x20))
            {
                throw new ArgumentException(
                    "Scene name is invalid.",
                    nameof(sceneName));
            }
            EntryGateId = KeyframeAdapterEnvelope.RequireId(
                entryGateId,
                nameof(entryGateId));
            if (sceneEpoch < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sceneEpoch));
            }

            RequireSha(gameBuildSha256, nameof(gameBuildSha256));
            RequireSha(manifestSha256, nameof(manifestSha256));
            RequireSha(
                baselineObjectSha256,
                nameof(baselineObjectSha256));
            RequireSha(
                journalHeadSha256,
                nameof(journalHeadSha256));
            RequireSha(
                adapterManifestSha256,
                nameof(adapterManifestSha256));
            RequireSha(
                semanticSnapshotSha256,
                nameof(semanticSnapshotSha256));
            RequireSha(rngStateSha256, nameof(rngStateSha256));

            var ordered = (adapters
                           ?? throw new ArgumentNullException(
                               nameof(adapters)))
                .OrderBy(
                    value => value.AdapterId,
                    StringComparer.Ordinal)
                .ToArray();
            if (ordered.Length == 0
                || ordered.Length > 128
                || ordered.Select(value => value.AdapterId)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != ordered.Length)
            {
                throw new ArgumentException(
                    "Keyframe adapters must be non-empty, unique, and bounded.",
                    nameof(adapters));
            }

            SchemaVersion = schemaVersion;
            Tier = tier;
            Status = status;
            CaptureMovieTick = captureMovieTick;
            SceneName = sceneName;
            SceneEpoch = sceneEpoch;
            GameBuildSha256 = gameBuildSha256;
            ManifestSha256 = manifestSha256;
            BaselineObjectSha256 = baselineObjectSha256;
            JournalHeadSha256 = journalHeadSha256;
            AdapterManifestSha256 = adapterManifestSha256;
            SemanticSnapshotSha256 = semanticSnapshotSha256;
            RngStateSha256 = rngStateSha256;
            Adapters =
                new ReadOnlyCollection<KeyframeAdapterEnvelope>(
                    ordered);
        }

        public int SchemaVersion { get; }
        public string KeyframeId { get; }
        public KeyframeSupportTier Tier { get; }
        public KeyframeStatus Status { get; }
        public long CaptureMovieTick { get; }
        public string SceneName { get; }
        public string EntryGateId { get; }
        public int SceneEpoch { get; }
        public string GameBuildSha256 { get; }
        public string ManifestSha256 { get; }
        public string BaselineObjectSha256 { get; }
        public string JournalHeadSha256 { get; }
        public string AdapterManifestSha256 { get; }
        public string SemanticSnapshotSha256 { get; }
        public string RngStateSha256 { get; }
        public IReadOnlyList<KeyframeAdapterEnvelope> Adapters { get; }

        private static void RequireSha(
            string value,
            string name)
        {
            if (!MovieProtocolV1.IsLowerSha256(value))
            {
                throw new ArgumentException(
                    "A lowercase SHA-256 is required.",
                    name);
            }
        }
    }

    public sealed class KeyframeAdapterManifestEntry
    {
        public KeyframeAdapterManifestEntry(
            string adapterId,
            int schemaVersion,
            bool isRequired,
            KeyframeSupportTier minimumTier,
            string buildProfileId)
        {
            AdapterId = KeyframeAdapterEnvelope.RequireId(
                adapterId,
                nameof(adapterId));
            if (schemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(schemaVersion));
            }
            if (!Enum.IsDefined(
                    typeof(KeyframeSupportTier),
                    minimumTier)
                || minimumTier
                == KeyframeSupportTier.ReplayOnly)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumTier));
            }
            BuildProfileId =
                KeyframeAdapterEnvelope.RequireId(
                    buildProfileId,
                    nameof(buildProfileId));
            SchemaVersion = schemaVersion;
            IsRequired = isRequired;
            MinimumTier = minimumTier;
        }

        public string AdapterId { get; }
        public int SchemaVersion { get; }
        public bool IsRequired { get; }
        public KeyframeSupportTier MinimumTier { get; }
        public string BuildProfileId { get; }
    }

    public sealed class KeyframeAdapterManifest
    {
        public const int CurrentSchemaVersion = 1;

        public KeyframeAdapterManifest(
            int schemaVersion,
            IEnumerable<KeyframeAdapterManifestEntry> adapters)
        {
            if (schemaVersion != CurrentSchemaVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(schemaVersion));
            }
            var ordered = (adapters
                           ?? throw new ArgumentNullException(
                               nameof(adapters)))
                .OrderBy(
                    value => value.AdapterId,
                    StringComparer.Ordinal)
                .ToArray();
            if (ordered.Length > 128
                || ordered.Select(value => value.AdapterId)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != ordered.Length)
            {
                throw new ArgumentException(
                    "Adapter manifest entries must be unique and bounded.",
                    nameof(adapters));
            }
            SchemaVersion = schemaVersion;
            Adapters =
                new ReadOnlyCollection<
                    KeyframeAdapterManifestEntry>(ordered);
        }

        public int SchemaVersion { get; }
        public IReadOnlyList<KeyframeAdapterManifestEntry> Adapters
        {
            get;
        }
    }

    public sealed class KeyframeIndexEntry
    {
        public KeyframeIndexEntry(
            string replaySaveId,
            string keyframeId,
            long keyframeMovieTick,
            long targetMovieTick)
        {
            ReplaySaveId = KeyframeAdapterEnvelope.RequireId(
                replaySaveId,
                nameof(replaySaveId));
            KeyframeId = KeyframeAdapterEnvelope.RequireId(
                keyframeId,
                nameof(keyframeId));
            if (keyframeMovieTick < 0
                || targetMovieTick < keyframeMovieTick)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(keyframeMovieTick));
            }
            KeyframeMovieTick = keyframeMovieTick;
            TargetMovieTick = targetMovieTick;
        }

        public string ReplaySaveId { get; }
        public string KeyframeId { get; }
        public long KeyframeMovieTick { get; }
        public long TargetMovieTick { get; }
    }

    public sealed class KeyframeArtifactIndex
    {
        public const int CurrentSchemaVersion = 1;

        public KeyframeArtifactIndex(
            int schemaVersion,
            IEnumerable<KeyframeIndexEntry> entries)
        {
            if (schemaVersion != CurrentSchemaVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(schemaVersion));
            }
            var ordered = (entries
                           ?? throw new ArgumentNullException(
                               nameof(entries)))
                .OrderBy(
                    value => value.ReplaySaveId,
                    StringComparer.Ordinal)
                .ToArray();
            if (ordered.Length > 100000
                || ordered.Select(value => value.ReplaySaveId)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != ordered.Length)
            {
                throw new ArgumentException(
                    "Keyframe index entries must be unique and bounded.",
                    nameof(entries));
            }
            SchemaVersion = schemaVersion;
            Entries =
                new ReadOnlyCollection<KeyframeIndexEntry>(
                    ordered);
        }

        public int SchemaVersion { get; }
        public IReadOnlyList<KeyframeIndexEntry> Entries { get; }

        public KeyframeIndexEntry? Find(string replaySaveId)
        {
            return Entries.FirstOrDefault(
                value => string.Equals(
                    value.ReplaySaveId,
                    replaySaveId,
                    StringComparison.Ordinal));
        }
    }
}
