using System;
using HollowKnightTAS.Core.ReplaySave;

namespace HollowKnightTAS.Runtime.ReplaySave
{
    internal static class ColdReplayPrefixPolicy
    {
        internal static long RequiredTicks(ColdRestoreOperationKind kind, long checkpointTick)
        {
            if (checkpointTick < 0) throw new ArgumentOutOfRangeException(nameof(checkpointTick));
            switch (kind)
            {
                case ColdRestoreOperationKind.RestoreReplaySave:
                    return checked(checkpointTick + 1);
                case ColdRestoreOperationKind.SeekMovieTick:
                case ColdRestoreOperationKind.ApplyBranchAndSeek:
                    // Zero-input canonical identity still binds the full movie
                    // header (game/API, environment and baseline), not old inputs.
                    return 0;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }
}
