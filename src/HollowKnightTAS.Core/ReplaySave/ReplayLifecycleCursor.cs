using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Core.ReplaySave
{
    // Owned by the game-thread boundary pump. Claim before calling a native
    // operation; repeated pumps return no new claim until completion is observed.
    public sealed class ReplayLifecycleCursor
    {
        private readonly IReadOnlyList<ReplayLifecycleRecord> requests;
        private int next;
        private long observedMovieTick = -1;
        private long startedNativeFrame;

        public ReplayLifecycleCursor(ReplayLifecycleLog log, MovieDocument movie)
        {
            if (log == null) throw new ArgumentNullException(nameof(log));
            if (!log.IsCompleted) throw new InvalidDataException("Cannot replay an unfinished lifecycle log.");
            log.VerifyInputPrefixes(movie);
            requests = log.Records.Select(record => Request(record, record.AfterMovieTick, record.InputPrefixSha256)).ToArray();
        }

        public ReplayLifecycleCursor(ReplayLifecycleTimelineEditResult edit)
        {
            if (edit == null) throw new ArgumentNullException(nameof(edit));
            requests = edit.Operations.Select(operation => Request(operation.Source,
                operation.AfterMovieTick, operation.InputPrefixSha256)).ToArray();
            long previousBoundary = -1;
            for (var i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                if (request.Sequence != i || request.AfterMovieTick < previousBoundary
                    || request.InputPrefixSha256 != MoviePrefixIdentity.ComputeSha256(edit.InputEdit.Movie, request.AfterMovieTick + 1))
                    throw new InvalidDataException("Edited lifecycle plan does not match its input movie.");
                previousBoundary = request.AfterMovieTick;
            }
        }

        private static ReplayLifecycleRecord Request(ReplayLifecycleRecord source, long tick, string prefix) =>
            new ReplayLifecycleRecord(source.Sequence, tick, prefix, source.Kind, source.Slot,
                source.SlotObjectSha256, ReplayLifecycleOutcome.Waiting, 0, string.Empty, source.ModdedSlotObjectSha256);

        public ReplayLifecycleCursor(ReplayLifecycleExecutionPlan plan, long targetMovieTick)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var count = plan.Movie.Commands.OfType<FrameRunCommand>().Sum(x => x.FrameCount);
            if (targetMovieTick < 0 || targetMovieTick >= count) throw new ArgumentOutOfRangeException(nameof(targetMovieTick));
            requests = plan.Requests.Where(x => x.AfterMovieTick <= targetMovieTick).ToArray();
        }

        public ReplayLifecycleRecord? Active { get; private set; }
        public string Failure { get; private set; } = string.Empty;
        public int CompletedCount => next;
        public bool IsComplete => Failure.Length == 0 && Active == null && next == requests.Count;
        public long LastNativeFrameCount { get; private set; }

        public bool CanAdvanceInput(long completedMovieTick)
        {
            ObserveBoundary(completedMovieTick);
            return Failure.Length == 0 && Active == null
                && (next == requests.Count || requests[next].AfterMovieTick > completedMovieTick);
        }

        public ReplayLifecycleRecord? Claim(long completedMovieTick, long nativeFrame)
        {
            ObserveBoundary(completedMovieTick);
            if (nativeFrame < 0) throw new ArgumentOutOfRangeException(nameof(nativeFrame));
            if (Failure.Length != 0 || Active != null || next == requests.Count
                || requests[next].AfterMovieTick != completedMovieTick) return null;
            Active = requests[next];
            startedNativeFrame = nativeFrame;
            return Active;
        }

        public ReplayLifecycleRecord Complete(int sequence, long nativeFrame)
        {
            RequireActive(sequence);
            if (nativeFrame < startedNativeFrame)
            {
                Fail(sequence, "Native frame counter moved backwards during lifecycle operation.");
                throw new InvalidOperationException(Failure);
            }
            LastNativeFrameCount = nativeFrame - startedNativeFrame;
            var completed = new ReplayLifecycleRecord(Active!.Sequence, Active.AfterMovieTick,
                Active.InputPrefixSha256, Active.Kind, Active.Slot, Active.SlotObjectSha256,
                ReplayLifecycleOutcome.Completed, LastNativeFrameCount, string.Empty, Active.ModdedSlotObjectSha256);
            Active = null;
            next++;
            return completed;
        }

        public void Fail(int sequence, string reason)
        {
            RequireActive(sequence);
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Failure reason is required.", nameof(reason));
            Failure = reason;
            // Retain Active and next for diagnostics; failure never consumes or
            // retries an operation that may already have changed native state.
        }

        private void RequireActive(int sequence)
        {
            if (Failure.Length != 0 || Active == null || Active.Sequence != sequence)
                throw new InvalidOperationException("Lifecycle completion does not match the active operation.");
        }

        private void ObserveBoundary(long tick)
        {
            if (Failure.Length != 0) return;
            if (tick < -1 || tick < observedMovieTick
                || (Active != null && tick != Active.AfterMovieTick)
                || (next < requests.Count && tick > requests[next].AfterMovieTick))
            {
                Failure = "Input advanced across an uncompleted lifecycle boundary or moved backwards.";
                throw new InvalidOperationException(Failure);
            }
            observedMovieTick = tick;
        }
    }
}
