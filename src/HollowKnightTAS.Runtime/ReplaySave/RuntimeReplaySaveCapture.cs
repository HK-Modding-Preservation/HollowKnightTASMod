using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.Recording;
using HollowKnightTAS.Core.ReplaySave;
using HollowKnightTAS.Core.Serialization;
using HollowKnightTAS.Runtime.Playback;
using HollowKnightTAS.Runtime.State;

namespace HollowKnightTAS.Runtime.ReplaySave
{
    public sealed class RuntimeReplaySaveCapture
    {
        private readonly byte[] manifestBytes;
        private readonly string manifestSha256;
        private readonly string gameVersion;
        private readonly string apiVersion;

        public RuntimeReplaySaveCapture(
            byte[] manifestBytes,
            string manifestSha256,
            string gameVersion,
            string apiVersion)
        {
            if (manifestBytes == null || manifestBytes.Length == 0)
            {
                throw new ArgumentException(
                    "Canonical manifest bytes are required.",
                    nameof(manifestBytes));
            }

            this.manifestBytes = (byte[])manifestBytes.Clone();
            if (!MovieProtocolV1.IsLowerSha256(manifestSha256))
            {
                throw new ArgumentException(
                    "A canonical manifest SHA-256 is required.",
                    nameof(manifestSha256));
            }

            this.manifestSha256 = manifestSha256;
            if (!string.Equals(
                    Sha256Utility.ComputeHex(this.manifestBytes),
                    this.manifestSha256,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Manifest bytes do not match their SHA-256.",
                    nameof(manifestBytes));
            }

            this.gameVersion = gameVersion
                               ?? throw new ArgumentNullException(nameof(gameVersion));
            this.apiVersion = apiVersion
                              ?? throw new ArgumentNullException(nameof(apiVersion));
        }

        public ReplaySaveCommit BuildCommit(
            ReplaySaveRequest request,
            RuntimeReplayJournalFreezeResult frozen,
            SnapshotCaptureResult targetCapture,
            int autoRetentionCount,
            DateTimeOffset createdAtUtc)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (frozen == null || !frozen.Success)
            {
                throw new ArgumentException(
                    "A successful frozen journal prefix is required.",
                    nameof(frozen));
            }

            if (targetCapture == null
                || !targetCapture.Success
                || targetCapture.CanonicalBytes == null)
            {
                throw new ArgumentException(
                    "A successful target semantic capture is required.",
                    nameof(targetCapture));
            }

            var replaySaveId = request.RequestId.StartsWith(
                "request-",
                StringComparison.Ordinal)
                ? "save-" + request.RequestId.Substring("request-".Length)
                : "save-" + request.RequestId;
            var movie = BuildMovie(
                frozen,
                replaySaveId + ".hktas");
            var movieBytes = new MovieCanonicalWriter().WriteUtf8(movie);
            var records = ReadRecords(frozen);
            var ledgerSummary = BuildLedgerSummary(
                records,
                frozen.JournalHeadSha256,
                targetCapture);
            var sceneName =
                targetCapture.Snapshot!.Values["scene.name"].DisplayValue;
            var commit = ReplaySaveCommit.Create(
                replaySaveId,
                request.Label,
                request.Reason,
                request.RequestedAtUtc,
                createdAtUtc,
                request.RequestedAtMovieTick,
                frozen.EffectiveMovieTick,
                manifestBytes,
                frozen.BaselineBundleBytes!,
                movieBytes,
                frozen.SegmentBytes,
                targetCapture.CanonicalBytes,
                ledgerSummary,
                sceneName,
                targetCapture.Stamp.SceneEpoch,
                request.Reason == ReplaySaveReason.AutomaticInterval
                    ? autoRetentionCount
                    : 0);
            return frozen.Lifecycle == null ? commit : commit.WithLifecycle(frozen.Lifecycle, frozen.LifecycleObjects);
        }

        public MovieDocument BuildMovie(
            RuntimeReplayJournalFreezeResult frozen,
            string sourceName)
        {
            if (frozen == null || !frozen.Success)
            {
                throw new ArgumentException(
                    "A successful frozen journal prefix is required.",
                    nameof(frozen));
            }

            if (string.IsNullOrWhiteSpace(sourceName))
            {
                throw new ArgumentException(
                    "A movie source name is required.",
                    nameof(sourceName));
            }

            var records = ReadRecords(frozen);
            return ReplayMovieBuilder.Build(
                records,
                frozen.Commands.Select(
                    value => new ReplayMovieEvent(
                        value.BeforeMovieTick,
                        new CheckpointCommand(
                            value.CheckpointIdentifier,
                            new MovieSourceSpan(
                                sourceName,
                                1,
                                1,
                                1)))),
                sourceName,
                gameVersion,
                apiVersion,
                manifestSha256,
                frozen.BaselineId,
                frozen.BaselineSemanticSha256);
        }

        private static IReadOnlyList<JournalRecord> ReadRecords(
            RuntimeReplayJournalFreezeResult frozen)
        {
            var records = new List<JournalRecord>();
            foreach (var bytes in frozen.SegmentBytes)
            {
                records.AddRange(
                    ReplayJournalSegmentCodec.Deserialize(bytes).Records);
            }

            if (records.Count == 0
                || records[records.Count - 1].MovieTick
                   != frozen.EffectiveMovieTick)
            {
                throw new InvalidOperationException(
                    "Frozen journal records do not reach the target tick.");
            }

            return records;
        }

        private static byte[] BuildLedgerSummary(
            IReadOnlyList<JournalRecord> records,
            string journalHeadSha256,
            SnapshotCaptureResult targetCapture)
        {
            var first = records[0];
            var last = records[records.Count - 1];
            var builder = new StringBuilder(1024);
            builder.Append("{\"firstFixedTick\":");
            builder.Append(
                first.Stamp.FixedTick.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"firstInputTick\":");
            builder.Append(
                first.Stamp.InputTick.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"firstMovieTick\":");
            builder.Append(
                first.MovieTick.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"firstSceneEpoch\":");
            builder.Append(
                first.Stamp.SceneEpoch.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"firstVisualTick\":");
            builder.Append(
                first.Stamp.VisualTick.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"journalHeadSha256\":");
            CanonicalJsonWriter.AppendString(builder, journalHeadSha256);
            builder.Append(",\"lastFixedTick\":");
            builder.Append(
                targetCapture.Stamp.FixedTick.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"lastInputTick\":");
            builder.Append(
                last.Stamp.InputTick.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"lastMovieTick\":");
            builder.Append(
                last.MovieTick.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"lastSceneEpoch\":");
            builder.Append(
                targetCapture.Stamp.SceneEpoch.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"lastVisualTick\":");
            builder.Append(
                targetCapture.Stamp.VisualTick.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"recordCount\":");
            builder.Append(
                records.Count.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"schemaVersion\":1}");
            return new UTF8Encoding(false).GetBytes(builder.ToString());
        }
    }
}
