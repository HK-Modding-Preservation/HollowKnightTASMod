using System;

namespace HollowKnightTAS.Core.ReplaySave
{
    public readonly struct ReplayRestoreHandle : IEquatable<ReplayRestoreHandle>
    {
        public ReplayRestoreHandle(string id)
        {
            Id = ReplaySaveDescriptor.RequireIdentifier(id, nameof(id));
        }

        public string Id { get; }

        public bool Equals(ReplayRestoreHandle other)
        {
            return string.Equals(Id, other.Id, StringComparison.Ordinal);
        }

        public override bool Equals(object? value)
        {
            return value is ReplayRestoreHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Id == null
                ? 0
                : StringComparer.Ordinal.GetHashCode(Id);
        }
    }

    public enum ReplayRestorePhase : byte
    {
        Validating = 1,
        PlanningAcceleration = 2,
        AwaitingOverwriteApproval = 3,
        InstallingBaseline = 4,
        LoadingBaseline = 5,
        AligningBaseline = 6,
        ReplayingPrefix = 7,
        VerifyingTarget = 8,
        Paused = 9,
        Completed = 10,
        Cancelled = 11,
        Failed = 12,
        ReturningToMenu = 13,
        BaselineReady = 14,
        PausedAtTarget = 15
    }

    public enum ReplayRestoreStrategy : byte
    {
        FunctionalReplayRestore = 1,
        VanillaEquivalentColdReplay = 2
    }

    public enum ReplayRestoreEquivalenceClass : byte
    {
        FunctionalOnly = 1,
        VanillaEquivalent = 2
    }

    public enum ReplayRestoreTargetVerification : byte
    {
        ExactSavedSemantic = 1,
        ReconstructedObservation = 2
    }

    public sealed class ReplayRestoreProgress
    {
        public ReplayRestoreProgress(
            ReplayRestoreHandle handle,
            ReplayRestorePhase phase,
            ReplaySaveStatus status,
            long currentMovieTick,
            long targetMovieTick,
            long nextMovieTick,
            bool requiresOverwriteApproval,
            double fraction,
            string detail,
            string expectedSemanticSha256 = "",
            string actualSemanticSha256 = "",
            bool? bindingRestoreEquivalent = null,
            bool? settingsRestoreEquivalent = null,
            string semanticProjectionId = "",
            string expectedVerificationSha256 = "",
            string actualVerificationSha256 = "",
            bool? strictSemanticEquivalent = null,
            ReplayRestoreStrategy strategy =
                ReplayRestoreStrategy.FunctionalReplayRestore,
            ReplayRestoreEquivalenceClass equivalenceClass =
                ReplayRestoreEquivalenceClass.FunctionalOnly,
            string operationId = "",
            ReplayRestoreTargetVerification targetVerification =
                ReplayRestoreTargetVerification.ExactSavedSemantic)
        {
            if (!Enum.IsDefined(typeof(ReplayRestorePhase), phase))
            {
                throw new ArgumentOutOfRangeException(nameof(phase));
            }

            if (!Enum.IsDefined(typeof(ReplaySaveStatus), status))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            if (!Enum.IsDefined(typeof(ReplayRestoreStrategy), strategy))
            {
                throw new ArgumentOutOfRangeException(nameof(strategy));
            }

            if (!Enum.IsDefined(
                    typeof(ReplayRestoreEquivalenceClass),
                    equivalenceClass))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(equivalenceClass));
            }

            if (!Enum.IsDefined(
                    typeof(ReplayRestoreTargetVerification),
                    targetVerification))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetVerification));
            }

            if (fraction < 0d || fraction > 1d
                || double.IsNaN(fraction)
                || double.IsInfinity(fraction))
            {
                throw new ArgumentOutOfRangeException(nameof(fraction));
            }

            Handle = handle;
            Phase = phase;
            Status = status;
            CurrentMovieTick = currentMovieTick;
            TargetMovieTick = targetMovieTick;
            NextMovieTick = nextMovieTick;
            RequiresOverwriteApproval = requiresOverwriteApproval;
            Fraction = fraction;
            Detail = detail ?? string.Empty;
            ExpectedSemanticSha256 = expectedSemanticSha256 ?? string.Empty;
            ActualSemanticSha256 = actualSemanticSha256 ?? string.Empty;
            BindingRestoreEquivalent = bindingRestoreEquivalent;
            SettingsRestoreEquivalent = settingsRestoreEquivalent;
            SemanticProjectionId = semanticProjectionId ?? string.Empty;
            ExpectedVerificationSha256 =
                expectedVerificationSha256 ?? string.Empty;
            ActualVerificationSha256 =
                actualVerificationSha256 ?? string.Empty;
            StrictSemanticEquivalent = strictSemanticEquivalent;
            Strategy = strategy;
            EquivalenceClass = equivalenceClass;
            OperationId = operationId == null
                ? string.Empty
                : ColdRestoreIntent.RequireOptionalIdentifier(
                    operationId,
                    nameof(operationId));
            TargetVerification = targetVerification;
        }

        public ReplayRestoreHandle Handle { get; }
        public ReplayRestorePhase Phase { get; }
        public ReplaySaveStatus Status { get; }
        public long CurrentMovieTick { get; }
        public long TargetMovieTick { get; }
        public long NextMovieTick { get; }
        public bool RequiresOverwriteApproval { get; }
        public double Fraction { get; }
        public string Detail { get; }
        public string ExpectedSemanticSha256 { get; }
        public string ActualSemanticSha256 { get; }
        public bool? BindingRestoreEquivalent { get; }
        public bool? SettingsRestoreEquivalent { get; }
        public string SemanticProjectionId { get; }
        public string ExpectedVerificationSha256 { get; }
        public string ActualVerificationSha256 { get; }
        public bool? StrictSemanticEquivalent { get; }
        public ReplayRestoreStrategy Strategy { get; }
        public ReplayRestoreEquivalenceClass EquivalenceClass { get; }
        public string OperationId { get; }
        public ReplayRestoreTargetVerification TargetVerification { get; }
        public bool IsTerminal =>
            Phase == ReplayRestorePhase.Completed
            || Phase == ReplayRestorePhase.Cancelled
            || Phase == ReplayRestorePhase.Failed;
    }

    public sealed class ReplayRestoreResult
    {
        public ReplayRestoreResult(
            bool success,
            ReplayRestoreProgress progress,
            string error)
        {
            Success = success;
            Progress = progress
                       ?? throw new ArgumentNullException(nameof(progress));
            Error = error ?? string.Empty;
        }

        public bool Success { get; }
        public ReplayRestoreProgress Progress { get; }
        public string Error { get; }
    }
}
