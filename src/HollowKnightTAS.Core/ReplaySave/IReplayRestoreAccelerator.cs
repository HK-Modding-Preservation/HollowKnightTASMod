using System.Threading;

namespace HollowKnightTAS.Core.ReplaySave
{
    public interface IReplayRestoreAccelerator
    {
        ReplayRestoreAccelerationPlan TryPlan(ReplaySaveDescriptor save);

        ReplayRestoreAccelerationResult TryRestore(
            ReplayRestoreAccelerationPlan plan,
            CancellationToken cancellationToken);
    }
}
