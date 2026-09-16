using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace HollowKnightTAS.Core.ReplaySave
{
    public sealed class ReplaySaveScheduler
    {
        private readonly Queue<ReplaySaveRequest> pending =
            new Queue<ReplaySaveRequest>();
        private AutoSavePolicy policy;
        private long lastCommittedMovieTick = -1;
        private long nextAutomaticMovieTick;
        private long requestSequence;

        public ReplaySaveScheduler(AutoSavePolicy policy)
        {
            this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
            nextAutomaticMovieTick = checked(policy.IntervalMovieTicks - 1);
        }

        public AutoSavePolicy Policy => policy;
        public long LastCommittedMovieTick => lastCommittedMovieTick;
        public long NextAutomaticMovieTick => nextAutomaticMovieTick;
        public int PendingCount => pending.Count;
        public IReadOnlyList<ReplaySaveRequest> Pending =>
            new ReadOnlyCollection<ReplaySaveRequest>(
                new List<ReplaySaveRequest>(pending));

        public void ResetForBaseline()
        {
            pending.Clear();
            lastCommittedMovieTick = -1;
            nextAutomaticMovieTick = checked(policy.IntervalMovieTicks - 1);
        }

        public AutoSavePolicyResult SetPolicy(AutoSavePolicy value)
        {
            return SetPolicy(value, null);
        }

        public AutoSavePolicyResult SetPolicy(AutoSavePolicy value, long? currentMovieTick)
        {
            if (value == null)
            {
                return new AutoSavePolicyResult(
                    false,
                    policy,
                    "Auto-save policy is required.");
            }

            var boundary = currentMovieTick ?? lastCommittedMovieTick;
            if (boundary < -1 || boundary < lastCommittedMovieTick)
                return new AutoSavePolicyResult(false, policy, "Auto-save policy boundary cannot move backwards.");
            var next = checked(boundary + value.IntervalMovieTicks);
            policy = value;
            lastCommittedMovieTick = boundary;
            nextAutomaticMovieTick = next;
            return new AutoSavePolicyResult(true, policy, string.Empty);
        }

        public ReplaySaveRequestResult Request(
            string label,
            ReplaySaveReason reason,
            DateTimeOffset requestedAtUtc,
            bool requireSubsequentCommittedTick = false)
        {
            if (reason == ReplaySaveReason.AutomaticInterval)
            {
                return new ReplaySaveRequestResult(
                    false,
                    string.Empty,
                    ReplaySaveStatus.Failed,
                    "Automatic requests are created only by committed movie ticks.");
            }

            try
            {
                var request = CreateRequest(
                    label,
                    reason,
                    requestedAtUtc,
                    lastCommittedMovieTick,
                    requireSubsequentCommittedTick);
                pending.Enqueue(request);
                return new ReplaySaveRequestResult(
                    true,
                    request.RequestId,
                    ReplaySaveStatus.Pending,
                    string.Empty);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is OverflowException)
            {
                return new ReplaySaveRequestResult(
                    false,
                    string.Empty,
                    ReplaySaveStatus.Failed,
                    exception.Message);
            }
        }

        public ReplaySaveRequest? OnMovieTickCommitted(
            long movieTick,
            DateTimeOffset committedAtUtc)
        {
            if (movieTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(movieTick));
            }

            if (movieTick <= lastCommittedMovieTick)
            {
                throw new InvalidOperationException(
                    "Committed movie ticks must strictly advance.");
            }

            lastCommittedMovieTick = movieTick;
            if (!policy.Enabled || movieTick < nextAutomaticMovieTick)
            {
                return null;
            }

            var request = CreateRequest(
                "Auto "
                + movieTick.ToString("D12", CultureInfo.InvariantCulture),
                ReplaySaveReason.AutomaticInterval,
                committedAtUtc,
                movieTick);
            pending.Enqueue(request);
            do
            {
                nextAutomaticMovieTick = checked(
                    nextAutomaticMovieTick
                    + policy.IntervalMovieTicks);
            }
            while (nextAutomaticMovieTick <= movieTick);

            return request;
        }

        public bool TryDequeueForSafeTick(
            long effectiveMovieTick,
            out ReplaySaveRequest? request)
        {
            if (effectiveMovieTick < 0)
            {
                request = null;
                return false;
            }

            if (pending.Count == 0
                || pending.Peek().MinimumEffectiveMovieTick
                   > effectiveMovieTick)
            {
                request = null;
                return false;
            }

            request = pending.Dequeue();
            return true;
        }

        public IReadOnlyList<ReplaySaveRequest> FailPendingOnShutdown()
        {
            var failed = new List<ReplaySaveRequest>(pending);
            pending.Clear();
            return new ReadOnlyCollection<ReplaySaveRequest>(failed);
        }

        private ReplaySaveRequest CreateRequest(
            string label,
            ReplaySaveReason reason,
            DateTimeOffset requestedAtUtc,
            long requestedAtMovieTick,
            bool requireSubsequentCommittedTick = false)
        {
            var sequence = checked(++requestSequence);
            var requestId =
                "request-"
                + requestedAtUtc
                    .ToUniversalTime()
                    .ToString(
                        "yyyyMMddTHHmmssfffffffZ",
                        CultureInfo.InvariantCulture)
                + "-"
                + sequence.ToString("D8", CultureInfo.InvariantCulture);
            return new ReplaySaveRequest(
                requestId,
                label,
                reason,
                requestedAtUtc,
                requestedAtMovieTick,
                requireSubsequentCommittedTick);
        }
    }
}
