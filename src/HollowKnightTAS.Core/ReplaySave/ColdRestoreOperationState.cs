using System;

namespace HollowKnightTAS.Core.ReplaySave
{
    public enum ColdRestoreOperationState : byte
    {
        Prepared = 1,
        SourceQuiesced = 2,
        SourceExited = 3,
        Launching = 4,
        NewSessionAttached = 5,
        IntentClaimed = 6,
        BaselineReady = 7,
        ReplayingPrefix = 8,
        PausedAtTarget = 9,
        Completed = 10,
        Cancelled = 11,
        Failed = 12
    }

    public static class ColdRestoreOperationStateMachine
    {
        public static bool IsTerminal(ColdRestoreOperationState state)
        {
            RequireState(state, nameof(state));
            return state == ColdRestoreOperationState.Completed
                   || state == ColdRestoreOperationState.Cancelled
                   || state == ColdRestoreOperationState.Failed;
        }

        public static bool CanTransition(
            ColdRestoreOperationState current,
            ColdRestoreOperationState next)
        {
            RequireState(current, nameof(current));
            RequireState(next, nameof(next));
            if (IsTerminal(current) || current == next)
            {
                return false;
            }

            if (next == ColdRestoreOperationState.Failed)
            {
                return true;
            }

            if (next == ColdRestoreOperationState.Cancelled)
            {
                // Cancelling after source exit aborts the owned target process;
                // it does not resurrect the source. A reached target is handed
                // back normally and must not be destroyed by a late cancel.
                return current != ColdRestoreOperationState.PausedAtTarget;
            }

            switch (current)
            {
                case ColdRestoreOperationState.Prepared:
                    return next == ColdRestoreOperationState.SourceQuiesced;
                case ColdRestoreOperationState.SourceQuiesced:
                    return next == ColdRestoreOperationState.SourceExited;
                case ColdRestoreOperationState.SourceExited:
                    return next == ColdRestoreOperationState.Launching
                           || next == ColdRestoreOperationState.NewSessionAttached;
                case ColdRestoreOperationState.Launching:
                    return next == ColdRestoreOperationState.NewSessionAttached;
                case ColdRestoreOperationState.NewSessionAttached:
                    return next == ColdRestoreOperationState.IntentClaimed;
                case ColdRestoreOperationState.IntentClaimed:
                    return next == ColdRestoreOperationState.BaselineReady;
                case ColdRestoreOperationState.BaselineReady:
                    return next == ColdRestoreOperationState.ReplayingPrefix;
                case ColdRestoreOperationState.ReplayingPrefix:
                    return next == ColdRestoreOperationState.PausedAtTarget;
                case ColdRestoreOperationState.PausedAtTarget:
                    return next == ColdRestoreOperationState.Completed;
                default:
                    return false;
            }
        }

        public static void RequireTransition(
            ColdRestoreOperationState current,
            ColdRestoreOperationState next)
        {
            if (!CanTransition(current, next))
            {
                throw new InvalidOperationException(
                    "Cold-restore operation transition is not allowed: "
                    + current
                    + " -> "
                    + next
                    + ".");
            }
        }

        public static void RequireActor(
            ColdRestoreOperationState state,
            ColdRestoreActorRole actorRole)
        {
            RequireState(state, nameof(state));
            if (!Enum.IsDefined(typeof(ColdRestoreActorRole), actorRole))
            {
                throw new ArgumentOutOfRangeException(nameof(actorRole));
            }

            if (state == ColdRestoreOperationState.Failed
                || state == ColdRestoreOperationState.Cancelled)
            {
                return;
            }

            var expected = state == ColdRestoreOperationState.SourceQuiesced
                           || state == ColdRestoreOperationState.IntentClaimed
                           || state == ColdRestoreOperationState.BaselineReady
                           || state == ColdRestoreOperationState.ReplayingPrefix
                           || state == ColdRestoreOperationState.PausedAtTarget
                ? ColdRestoreActorRole.Runtime
                : ColdRestoreActorRole.Companion;
            if (actorRole != expected)
            {
                throw new InvalidOperationException(
                    state
                    + " must be recorded by "
                    + expected
                    + ".");
            }
        }

        private static void RequireState(
            ColdRestoreOperationState state,
            string name)
        {
            if (!Enum.IsDefined(typeof(ColdRestoreOperationState), state))
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }
}
