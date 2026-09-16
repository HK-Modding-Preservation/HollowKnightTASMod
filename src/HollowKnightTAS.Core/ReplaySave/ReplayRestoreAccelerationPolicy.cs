using System;
using System.Threading;

namespace HollowKnightTAS.Core.ReplaySave
{
    public static class ReplayRestoreAccelerationPolicy
    {
        public static ReplayRestoreAccelerationPlan EvaluatePlan(
            IReplayRestoreAccelerator? accelerator,
            ReplaySaveDescriptor descriptor)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            if (accelerator == null)
            {
                return new ReplayRestoreAccelerationPlan(
                    ReplayRestoreAccelerationStatus.Unavailable,
                    0,
                    "No restore accelerator is registered.");
            }

            try
            {
                return accelerator.TryPlan(descriptor)
                       ?? new ReplayRestoreAccelerationPlan(
                           ReplayRestoreAccelerationStatus.Faulted,
                           0,
                           "Restore accelerator returned a null plan.");
            }
            catch (Exception exception)
            {
                return new ReplayRestoreAccelerationPlan(
                    ReplayRestoreAccelerationStatus.Faulted,
                    0,
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        public static ReplayRestoreAccelerationAttempt TryRestore(
            IReplayRestoreAccelerator? accelerator,
            ReplayRestoreAccelerationPlan plan,
            CancellationToken cancellationToken)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (accelerator == null
                || plan.Status != ReplayRestoreAccelerationStatus.Ready)
            {
                return new ReplayRestoreAccelerationAttempt(
                    attempted: false,
                    success: false,
                    faulted: false,
                    resumeMovieTick: 0,
                    detail:
                    "Full replay is required because no ready accelerator "
                    + "plan is available.");
            }

            try
            {
                var result = accelerator.TryRestore(
                    plan,
                    cancellationToken);
                if (result == null)
                {
                    return new ReplayRestoreAccelerationAttempt(
                        attempted: true,
                        success: false,
                        faulted: true,
                        resumeMovieTick: 0,
                        detail:
                        "Restore accelerator returned a null result.");
                }

                return new ReplayRestoreAccelerationAttempt(
                    attempted: true,
                    success: result.Success,
                    faulted: false,
                    resumeMovieTick: result.ResumeMovieTick,
                    detail: result.Detail);
            }
            catch (Exception exception)
            {
                return new ReplayRestoreAccelerationAttempt(
                    attempted: true,
                    success: false,
                    faulted: true,
                    resumeMovieTick: 0,
                    detail:
                    exception.GetType().Name + ": " + exception.Message);
            }
        }
    }

    public sealed class ReplayRestoreAccelerationAttempt
    {
        public ReplayRestoreAccelerationAttempt(
            bool attempted,
            bool success,
            bool faulted,
            long resumeMovieTick,
            string detail)
        {
            if (resumeMovieTick < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resumeMovieTick));
            }

            if (!attempted && (success || faulted || resumeMovieTick != 0))
            {
                throw new ArgumentException(
                    "A non-attempt cannot report success, fault, or a cursor.");
            }

            if (success && faulted)
            {
                throw new ArgumentException(
                    "An acceleration attempt cannot both succeed and fault.");
            }

            Attempted = attempted;
            Success = success;
            Faulted = faulted;
            ResumeMovieTick = resumeMovieTick;
            Detail = detail ?? string.Empty;
        }

        public bool Attempted { get; }
        public bool Success { get; }
        public bool Faulted { get; }
        public long ResumeMovieTick { get; }
        public string Detail { get; }
        public bool RequiresFullReplay => !Success;
    }
}
