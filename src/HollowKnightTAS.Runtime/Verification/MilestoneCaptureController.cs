using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GlobalEnums;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.Playback;
using HollowKnightTAS.Core.Verification;
using HollowKnightTAS.Runtime.Playback;
using HollowKnightTAS.Runtime.State;
using UnityEngine;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HollowKnightTAS.Runtime.Verification
{
    public sealed class MilestoneCaptureController
    {
        private const int ContextWindowSize = 32;
        private readonly RuntimeSnapshotCapture snapshotCapture =
            new RuntimeSnapshotCapture();
        private readonly Func<string>? rngStateSha256;
        private readonly Queue<VerificationLedgerEntry> recentLedger =
            new Queue<VerificationLedgerEntry>();
        private readonly Queue<PendingMilestone> pending =
            new Queue<PendingMilestone>();
        private readonly List<MilestoneRecord> milestones =
            new List<MilestoneRecord>();
        private InputSample lastInput;

        public IReadOnlyList<MilestoneRecord> Milestones =>
            new ReadOnlyCollection<MilestoneRecord>(milestones);

        public MilestoneCaptureController(
            Func<string>? rngStateSha256 = null)
        {
            this.rngStateSha256 = rngStateSha256;
        }

        public void AddBaseline(
            string milestoneId,
            long movieTick,
            SnapshotCaptureResult capture)
        {
            if (capture == null)
            {
                throw new ArgumentNullException(nameof(capture));
            }

            RequireCapture(capture, milestoneId);
            milestones.Add(
                new MilestoneRecord(
                    milestoneId,
                    movieTick,
                    capture.Stamp,
                    ReadScene(capture),
                    VerificationSnapshotNormalizer.Normalize(
                        capture.CanonicalBytes!),
                    lastInput,
                    Array.Empty<VerificationLedgerEntry>(),
                    ReadRngStateSha256()));
        }

        public void ObserveInput(
            ReplayInputObservation observation,
            TickStamp stamp)
        {
            if (observation == null)
            {
                throw new ArgumentNullException(nameof(observation));
            }

            lastInput = observation.Expected;
            recentLedger.Enqueue(
                new VerificationLedgerEntry(
                    observation.MovieTick,
                    stamp,
                    observation.Expected,
                    -1,
                    SingleBits.FromSingle(Time.time - Time.fixedTime),
                    USceneManager.GetActiveScene().name,
                    observation.PhysicalNoise
                        ? "input-with-physical-noise"
                        : "input",
                    ReadRngStateSha256()));
            while (recentLedger.Count > ContextWindowSize)
            {
                recentLedger.Dequeue();
            }
        }

        public void QueueEvent(PlaybackEvent playbackEvent)
        {
            if (playbackEvent == null)
            {
                throw new ArgumentNullException(nameof(playbackEvent));
            }

            // The explicit baseline already represents movie tick zero. A
            // marker is emitted during replay start and would otherwise be
            // captured on a later LateUpdate, making its semantic state depend
            // on when the runtime probe attached.
            if (playbackEvent.Command is MarkerCommand)
            {
                return;
            }

            pending.Enqueue(
                new PendingMilestone(
                    GetMilestoneId(playbackEvent),
                    playbackEvent.MovieTick));
        }

        public void CapturePending(TickStamp lateUpdateStamp)
        {
            while (pending.Count > 0)
            {
                var item = pending.Dequeue();
                var capture = snapshotCapture.Capture(lateUpdateStamp);
                RequireCapture(capture, item.Id);
                milestones.Add(
                    new MilestoneRecord(
                        item.Id,
                        item.MovieTick,
                        lateUpdateStamp,
                        ReadScene(capture),
                        VerificationSnapshotNormalizer.Normalize(
                            capture.CanonicalBytes!),
                        lastInput,
                        recentLedger.ToArray(),
                        ReadRngStateSha256()));
            }
        }

        public void AddEndpoint(
            long movieTick,
            SnapshotCaptureResult capture)
        {
            RequireCapture(capture, "settled-endpoint");
            milestones.Add(
                new MilestoneRecord(
                    "settled-endpoint",
                    movieTick,
                    capture.Stamp,
                    ReadScene(capture),
                    VerificationSnapshotNormalizer.Normalize(
                        capture.CanonicalBytes!),
                    lastInput,
                    recentLedger.ToArray(),
                    ReadRngStateSha256()));
        }

        private string ReadRngStateSha256()
        {
            return rngStateSha256 == null
                ? VerificationLedgerEntry.RngNotCaptured
                : rngStateSha256();
        }

        private static string GetMilestoneId(PlaybackEvent playbackEvent)
        {
            if (playbackEvent.Command is MarkerCommand marker)
            {
                return "marker:" + marker.Text;
            }

            if (playbackEvent.Command is CheckpointCommand checkpoint)
            {
                return "checkpoint:" + checkpoint.Identifier;
            }

            if (playbackEvent.Command is AssertCommand assertion)
            {
                return "assert:"
                       + assertion.SemanticPath
                       + assertion.Operator
                       + assertion.Value;
            }

            throw new InvalidOperationException(
                "Unsupported verification milestone command.");
        }

        private static string ReadScene(SnapshotCaptureResult capture)
        {
            if (capture.Snapshot == null
                || !capture.Snapshot.Values.TryGetValue(
                    "scene.name",
                    out var scene))
            {
                throw new InvalidOperationException(
                    "Verification snapshot has no scene.name.");
            }

            return scene.DisplayValue;
        }

        private static void RequireCapture(
            SnapshotCaptureResult capture,
            string milestoneId)
        {
            if (!capture.Success)
            {
                throw new InvalidOperationException(
                    "Milestone "
                    + milestoneId
                    + " capture failed at "
                    + capture.FailedProbeId
                    + ": "
                    + capture.Error);
            }
        }

        private sealed class PendingMilestone
        {
            public PendingMilestone(string id, long movieTick)
            {
                Id = id;
                MovieTick = movieTick;
            }

            public string Id { get; }
            public long MovieTick { get; }
        }
    }
}
