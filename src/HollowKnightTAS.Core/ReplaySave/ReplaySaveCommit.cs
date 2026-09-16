using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.State;

namespace HollowKnightTAS.Core.ReplaySave
{
    public sealed class ReplaySaveCommit
    {
        public const int MaximumManifestBytes = 16 * 1024 * 1024;
        public const int MaximumMovieBytes = 16 * 1024 * 1024;
        public const int MaximumLedgerSummaryBytes = 4 * 1024 * 1024;

        private readonly ReadOnlyDictionary<string, byte[]> objects;

        private ReplaySaveCommit(
            ReplaySaveDescriptor descriptor,
            IDictionary<string, byte[]> objects)
        {
            Descriptor = descriptor;
            this.objects = new ReadOnlyDictionary<string, byte[]>(
                new SortedDictionary<string, byte[]>(
                    objects.ToDictionary(
                        item => item.Key,
                        item => (byte[])item.Value.Clone(),
                        StringComparer.Ordinal),
                    StringComparer.Ordinal));
        }

        public ReplaySaveDescriptor Descriptor { get; }
        public IReadOnlyDictionary<string, byte[]> Objects => objects;

        public static ReplaySaveCommit Create(
            string replaySaveId,
            string label,
            ReplaySaveReason reason,
            DateTimeOffset requestedAtUtc,
            DateTimeOffset createdAtUtc,
            long requestedAtMovieTick,
            long effectiveMovieTick,
            byte[] manifestBytes,
            byte[] baselineBundleBytes,
            byte[] movieBytes,
            IEnumerable<byte[]> journalSegmentBytes,
            byte[] semanticSnapshotBytes,
            byte[] ledgerSummaryBytes,
            string sceneName,
            int sceneEpoch,
            int autoRetentionCount)
        {
            RequireBytes(
                manifestBytes,
                MaximumManifestBytes,
                nameof(manifestBytes));
            RequireBytes(
                baselineBundleBytes,
                BaselineBundle.MaximumBundleBytes,
                nameof(baselineBundleBytes));
            RequireBytes(movieBytes, MaximumMovieBytes, nameof(movieBytes));
            RequireBytes(
                semanticSnapshotBytes,
                SemanticSnapshotCanonicalizer.MaximumCanonicalBytes,
                nameof(semanticSnapshotBytes));
            RequireBytes(
                ledgerSummaryBytes,
                MaximumLedgerSummaryBytes,
                nameof(ledgerSummaryBytes));
            if (journalSegmentBytes == null)
            {
                throw new ArgumentNullException(nameof(journalSegmentBytes));
            }

            var baseline = BaselineBundleCodec.Deserialize(baselineBundleBytes);
            var targetSnapshot = SemanticSnapshotCanonicalizer.Deserialize(
                semanticSnapshotBytes);
            var targetSemanticSha = SemanticSnapshotHasher.ComputeSha256(
                targetSnapshot);

            var segmentHashes = new List<string>();
            var segmentObjects = new List<KeyValuePair<string, byte[]>>();
            var expectedSequence = 0;
            var expectedFirstTick = 0L;
            var expectedPrevious =
                ReplayJournalSegment.GenesisPreviousSha256;
            foreach (var segmentBytes in journalSegmentBytes)
            {
                RequireBytes(
                    segmentBytes,
                    ReplayJournalSegment.MaximumObjectBytes,
                    nameof(journalSegmentBytes));
                var segment = ReplayJournalSegmentCodec.Deserialize(segmentBytes);
                var hash = Sha256Utility.ComputeHex(segmentBytes);
                if (segment.Sequence != expectedSequence
                    || segment.FirstMovieTick != expectedFirstTick
                    || !string.Equals(
                        segment.PreviousObjectSha256,
                        expectedPrevious,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Journal segment sequence, tick range, or hash chain is invalid.");
                }

                segmentHashes.Add(hash);
                segmentObjects.Add(
                    new KeyValuePair<string, byte[]>(
                        hash,
                        (byte[])segmentBytes.Clone()));
                expectedSequence = checked(expectedSequence + 1);
                expectedFirstTick = checked(segment.LastMovieTick + 1);
                expectedPrevious = hash;
            }

            if (segmentHashes.Count == 0
                || expectedFirstTick - 1 != effectiveMovieTick)
            {
                throw new InvalidDataException(
                    "Journal prefix does not end at the effective movie tick.");
            }

            var manifestHash = Sha256Utility.ComputeHex(manifestBytes);
            var baselineObjectHash = Sha256Utility.ComputeHex(
                baselineBundleBytes);
            var movieHash = Sha256Utility.ComputeHex(movieBytes);
            var ledgerHash = Sha256Utility.ComputeHex(ledgerSummaryBytes);
            var descriptor = new ReplaySaveDescriptor(
                ReplaySaveDescriptor.CurrentSchemaVersion,
                replaySaveId,
                label,
                reason,
                requestedAtUtc,
                createdAtUtc,
                requestedAtMovieTick,
                effectiveMovieTick,
                manifestHash,
                baselineObjectHash,
                baseline.BaselineId,
                baseline.BaselineSemanticSha256,
                movieHash,
                segmentHashes[segmentHashes.Count - 1],
                segmentHashes,
                targetSemanticSha,
                ledgerHash,
                sceneName,
                sceneEpoch,
                autoRetentionCount);

            var objects = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            AddObject(objects, manifestHash, manifestBytes);
            AddObject(objects, baselineObjectHash, baselineBundleBytes);
            AddObject(objects, movieHash, movieBytes);
            AddObject(objects, targetSemanticSha, semanticSnapshotBytes);
            AddObject(objects, ledgerHash, ledgerSummaryBytes);
            foreach (var item in segmentObjects)
            {
                AddObject(objects, item.Key, item.Value);
            }

            var commit = new ReplaySaveCommit(descriptor, objects);
            var validation = ReplaySaveValidator.Validate(
                descriptor,
                commit.TryGetObject,
                manifestHash);
            if (!validation.Success)
            {
                throw new InvalidDataException(
                    "Replay-save commit failed validation: "
                    + validation.Detail);
            }

            return commit;
        }

        public ReplaySaveCommit WithLifecycle(ReplayLifecycleLog lifecycle,
            IReadOnlyDictionary<string, byte[]> slotObjects)
        {
            if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
            if (slotObjects == null) throw new ArgumentNullException(nameof(slotObjects));
            var combined = objects.ToDictionary(item => item.Key, item => (byte[])item.Value.Clone(), StringComparer.Ordinal);
            lifecycle.VerifySlotObjects(hash =>
            {
                if (!slotObjects.TryGetValue(hash, out var bytes) && !combined.TryGetValue(hash, out bytes)) return null;
                if (bytes == null || bytes.Length > ReplayLifecycleLog.MaximumSlotBytes)
                    throw new InvalidDataException("Lifecycle object exceeds its size limit.");
                AddObject(combined, hash, bytes);
                return combined[hash];
            });
            var encoded = lifecycle.Serialize();
            var hash = Sha256Utility.ComputeHex(encoded);
            AddObject(combined, hash, encoded);
            var commit = new ReplaySaveCommit(Descriptor.WithLifecycleObject(hash), combined);
            var validation = ReplaySaveValidator.Validate(commit.Descriptor, commit.TryGetObject, Descriptor.ManifestSha256);
            if (!validation.Success) throw new InvalidDataException("Lifecycle commit failed validation: " + validation.Detail);
            return commit;
        }

        public bool TryGetObject(string sha256, out byte[]? value)
        {
            if (objects.TryGetValue(sha256, out var bytes))
            {
                value = (byte[])bytes.Clone();
                return true;
            }

            value = null;
            return false;
        }

        private static void AddObject(
            IDictionary<string, byte[]> target,
            string hash,
            byte[] bytes)
        {
            if (target.TryGetValue(hash, out var existing))
            {
                if (!existing.SequenceEqual(bytes))
                {
                    throw new InvalidDataException(
                        "A content hash collision was detected.");
                }

                return;
            }

            target.Add(hash, (byte[])bytes.Clone());
        }

        private static void RequireBytes(
            byte[] value,
            int maximumBytes,
            string name)
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }

            if (value.Length == 0 || value.Length > maximumBytes)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }
}
