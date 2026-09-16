using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Core.ReplaySave
{
    public sealed class ReplaySaveDescriptor
    {
        public const int CurrentSchemaVersion = 1;
        public const int LifecycleSchemaVersion = 2;
        public const int MaximumLabelLength = 128;
        public const int MaximumSceneNameLength = 256;

        private readonly ReadOnlyCollection<string> journalSegmentObjectSha256s;

        public ReplaySaveDescriptor(
            int schemaVersion,
            string replaySaveId,
            string label,
            ReplaySaveReason reason,
            DateTimeOffset requestedAtUtc,
            DateTimeOffset createdAtUtc,
            long requestedAtMovieTick,
            long effectiveMovieTick,
            string manifestSha256,
            string baselineObjectSha256,
            string baselineId,
            string baselineSemanticSha256,
            string movieObjectSha256,
            string journalHeadSha256,
            IEnumerable<string> journalSegmentObjectSha256s,
            string semanticSnapshotSha256,
            string ledgerSummarySha256,
            string sceneName,
            int sceneEpoch,
            int autoRetentionCount,
            string? lifecycleObjectSha256 = null)
        {
            if (schemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            SchemaVersion = schemaVersion;
            if (schemaVersion == LifecycleSchemaVersion)
                LifecycleObjectSha256 = RequireSha256(lifecycleObjectSha256 ?? string.Empty, nameof(lifecycleObjectSha256));
            else if (lifecycleObjectSha256 != null)
                throw new ArgumentException("Lifecycle references require descriptor schema 2.", nameof(lifecycleObjectSha256));
            ReplaySaveId = RequireIdentifier(replaySaveId, nameof(replaySaveId));
            Label = RequireBounded(label, MaximumLabelLength, nameof(label));
            if (!Enum.IsDefined(typeof(ReplaySaveReason), reason))
            {
                throw new ArgumentOutOfRangeException(nameof(reason));
            }

            Reason = reason;
            RequestedAtUtc = requestedAtUtc.ToUniversalTime();
            CreatedAtUtc = createdAtUtc.ToUniversalTime();
            if (createdAtUtc < requestedAtUtc)
            {
                throw new ArgumentException(
                    "Creation time cannot precede request time.",
                    nameof(createdAtUtc));
            }

            if (requestedAtMovieTick < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(requestedAtMovieTick));
            }

            if (effectiveMovieTick < 0
                || effectiveMovieTick < requestedAtMovieTick)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(effectiveMovieTick),
                    effectiveMovieTick,
                    "Effective movie tick must be non-negative and not precede the request.");
            }

            RequestedAtMovieTick = requestedAtMovieTick;
            EffectiveMovieTick = effectiveMovieTick;
            ManifestSha256 = RequireSha256(manifestSha256, nameof(manifestSha256));
            BaselineObjectSha256 = RequireSha256(
                baselineObjectSha256,
                nameof(baselineObjectSha256));
            BaselineId = RequireIdentifier(baselineId, nameof(baselineId));
            BaselineSemanticSha256 = RequireSha256(
                baselineSemanticSha256,
                nameof(baselineSemanticSha256));
            MovieObjectSha256 = RequireSha256(
                movieObjectSha256,
                nameof(movieObjectSha256));
            JournalHeadSha256 = RequireSha256(
                journalHeadSha256,
                nameof(journalHeadSha256));
            SemanticSnapshotSha256 = RequireSha256(
                semanticSnapshotSha256,
                nameof(semanticSnapshotSha256));
            LedgerSummarySha256 = RequireSha256(
                ledgerSummarySha256,
                nameof(ledgerSummarySha256));
            SceneName = RequireBounded(
                sceneName,
                MaximumSceneNameLength,
                nameof(sceneName));
            if (sceneEpoch < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sceneEpoch));
            }

            SceneEpoch = sceneEpoch;
            if (reason == ReplaySaveReason.AutomaticInterval)
            {
                if (autoRetentionCount <= 0
                    || autoRetentionCount > AutoSavePolicy.MaximumRetentionCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(autoRetentionCount));
                }
            }
            else if (autoRetentionCount != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(autoRetentionCount),
                    "Only automatic entries carry a retention count.");
            }

            AutoRetentionCount = autoRetentionCount;
            if (journalSegmentObjectSha256s == null)
            {
                throw new ArgumentNullException(nameof(journalSegmentObjectSha256s));
            }

            var segments = journalSegmentObjectSha256s
                .Select(
                    value => RequireSha256(
                        value,
                        nameof(journalSegmentObjectSha256s)))
                .ToList();
            if (segments.Count == 0)
            {
                throw new ArgumentException(
                    "At least one journal segment is required.",
                    nameof(journalSegmentObjectSha256s));
            }

            if (!string.Equals(
                    segments[segments.Count - 1],
                    JournalHeadSha256,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Journal head must equal the final journal segment object hash.",
                    nameof(journalHeadSha256));
            }

            this.journalSegmentObjectSha256s =
                new ReadOnlyCollection<string>(segments);
        }

        public int SchemaVersion { get; }
        public string? LifecycleObjectSha256 { get; }
        public string ReplaySaveId { get; }
        public string Label { get; }
        public ReplaySaveReason Reason { get; }
        public DateTimeOffset RequestedAtUtc { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public long RequestedAtMovieTick { get; }
        public long EffectiveMovieTick { get; }
        public string ManifestSha256 { get; }
        public string BaselineObjectSha256 { get; }
        public string BaselineId { get; }
        public string BaselineSemanticSha256 { get; }
        public string MovieObjectSha256 { get; }
        public string JournalHeadSha256 { get; }
        public IReadOnlyList<string> JournalSegmentObjectSha256s =>
            journalSegmentObjectSha256s;
        public string SemanticSnapshotSha256 { get; }
        public string LedgerSummarySha256 { get; }
        public string SceneName { get; }
        public int SceneEpoch { get; }
        public int AutoRetentionCount { get; }

        public ReplaySaveDescriptor PinAsManual()
        {
            return new ReplaySaveDescriptor(
                SchemaVersion,
                ReplaySaveId,
                Label,
                ReplaySaveReason.Manual,
                RequestedAtUtc,
                CreatedAtUtc,
                RequestedAtMovieTick,
                EffectiveMovieTick,
                ManifestSha256,
                BaselineObjectSha256,
                BaselineId,
                BaselineSemanticSha256,
                MovieObjectSha256,
                JournalHeadSha256,
                JournalSegmentObjectSha256s,
                SemanticSnapshotSha256,
                LedgerSummarySha256,
                SceneName,
                SceneEpoch,
                0,
                LifecycleObjectSha256);
        }

        public ReplaySaveDescriptor WithLifecycleObject(string sha256)
        {
            return new ReplaySaveDescriptor(LifecycleSchemaVersion, ReplaySaveId, Label, Reason,
                RequestedAtUtc, CreatedAtUtc, RequestedAtMovieTick, EffectiveMovieTick,
                ManifestSha256, BaselineObjectSha256, BaselineId, BaselineSemanticSha256,
                MovieObjectSha256, JournalHeadSha256, JournalSegmentObjectSha256s,
                SemanticSnapshotSha256, LedgerSummarySha256, SceneName, SceneEpoch,
                AutoRetentionCount, sha256);
        }

        internal static string RequireSha256(string value, string name)
        {
            if (!MovieProtocolV1.IsLowerSha256(value))
            {
                throw new ArgumentException(
                    "A canonical lowercase SHA-256 is required.",
                    name);
            }

            return value;
        }

        internal static string RequireIdentifier(string value, string name)
        {
            if (!MovieProtocolV1.IsIdentifier(value))
            {
                throw new ArgumentException(
                    "A Movie v1 ASCII identifier is required.",
                    name);
            }

            return value;
        }

        private static string RequireBounded(
            string value,
            int maximumLength,
            string name)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.Length > maximumLength
                || !MovieProtocolV1.HasValidUtf16(value))
            {
                throw new ArgumentException(
                    "A non-empty bounded Unicode value is required.",
                    name);
            }

            return value;
        }
    }
}
