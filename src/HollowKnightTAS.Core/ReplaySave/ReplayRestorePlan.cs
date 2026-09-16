using System;

namespace HollowKnightTAS.Core.ReplaySave
{
    public enum ReplayRestoreAccelerationStatus : byte
    {
        Unavailable = 1,
        Ready = 2,
        Incompatible = 3,
        Faulted = 4
    }

    public sealed class ReplayRestoreAccelerationPlan
    {
        public ReplayRestoreAccelerationPlan(
            ReplayRestoreAccelerationStatus status,
            long resumeMovieTick,
            string detail,
            object? opaqueState = null)
        {
            if (!Enum.IsDefined(
                    typeof(ReplayRestoreAccelerationStatus),
                    status))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            if (resumeMovieTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(resumeMovieTick));
            }

            Status = status;
            ResumeMovieTick = resumeMovieTick;
            Detail = detail ?? string.Empty;
            OpaqueState = opaqueState;
        }

        public ReplayRestoreAccelerationStatus Status { get; }
        public long ResumeMovieTick { get; }
        public string Detail { get; }
        public object? OpaqueState { get; }
    }

    public sealed class ReplayRestoreAccelerationResult
    {
        public ReplayRestoreAccelerationResult(
            bool success,
            long resumeMovieTick,
            string detail)
        {
            if (resumeMovieTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(resumeMovieTick));
            }

            Success = success;
            ResumeMovieTick = resumeMovieTick;
            Detail = detail ?? string.Empty;
        }

        public bool Success { get; }
        public long ResumeMovieTick { get; }
        public string Detail { get; }
    }

    public sealed class ReplayRestorePlan
    {
        public ReplayRestorePlan(
            ReplaySaveDescriptor descriptor,
            ReplayRestoreAccelerationPlan acceleration)
        {
            Descriptor = descriptor
                         ?? throw new ArgumentNullException(nameof(descriptor));
            Acceleration = acceleration
                           ?? throw new ArgumentNullException(nameof(acceleration));
        }

        public ReplaySaveDescriptor Descriptor { get; }
        public ReplayRestoreAccelerationPlan Acceleration { get; }
        public bool UsesFullReplay =>
            Acceleration.Status != ReplayRestoreAccelerationStatus.Ready;
    }
}
