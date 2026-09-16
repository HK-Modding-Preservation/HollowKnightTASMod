using System;

namespace HollowKnightTAS.Core.ReplaySave
{
    public sealed class ReplaySaveRequest
    {
        public ReplaySaveRequest(
            string requestId,
            string label,
            ReplaySaveReason reason,
            DateTimeOffset requestedAtUtc,
            long requestedAtMovieTick,
            bool requireSubsequentCommittedTick = false)
        {
            RequestId = ReplaySaveDescriptor.RequireIdentifier(
                requestId,
                nameof(requestId));
            if (string.IsNullOrWhiteSpace(label)
                || label.Length > ReplaySaveDescriptor.MaximumLabelLength)
            {
                throw new ArgumentException(
                    "A non-empty label of at most 128 characters is required.",
                    nameof(label));
            }

            Label = label;
            if (!Enum.IsDefined(typeof(ReplaySaveReason), reason))
            {
                throw new ArgumentOutOfRangeException(nameof(reason));
            }

            Reason = reason;
            RequestedAtUtc = requestedAtUtc.ToUniversalTime();
            if (requestedAtMovieTick < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(requestedAtMovieTick));
            }

            RequestedAtMovieTick = requestedAtMovieTick;
            RequireSubsequentCommittedTick =
                requireSubsequentCommittedTick;
            MinimumEffectiveMovieTick =
                requireSubsequentCommittedTick
                    ? checked(requestedAtMovieTick + 1)
                    : Math.Max(0, requestedAtMovieTick);
        }

        public string RequestId { get; }
        public string Label { get; }
        public ReplaySaveReason Reason { get; }
        public DateTimeOffset RequestedAtUtc { get; }
        public long RequestedAtMovieTick { get; }
        public bool RequireSubsequentCommittedTick { get; }
        public long MinimumEffectiveMovieTick { get; }
    }

    public sealed class ReplaySaveRequestResult
    {
        public ReplaySaveRequestResult(
            bool accepted,
            string requestId,
            ReplaySaveStatus status,
            string error)
        {
            Accepted = accepted;
            RequestId = requestId ?? string.Empty;
            Status = status;
            Error = error ?? string.Empty;
        }

        public bool Accepted { get; }
        public string RequestId { get; }
        public ReplaySaveStatus Status { get; }
        public string Error { get; }
    }
}
