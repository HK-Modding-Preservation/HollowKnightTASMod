using HollowKnightTAS.Core.ReplaySave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.ReplaySave
{
    [TestClass]
    public sealed class ColdRestoreCancellationStateTests
    {
        [TestMethod]
        public void Cancellation_AllowsInFlightHandoffButProtectsReachedTarget()
        {
            foreach (var state in new[] {
                ColdRestoreOperationState.Prepared, ColdRestoreOperationState.SourceQuiesced,
                ColdRestoreOperationState.SourceExited, ColdRestoreOperationState.Launching,
                ColdRestoreOperationState.NewSessionAttached, ColdRestoreOperationState.IntentClaimed,
                ColdRestoreOperationState.BaselineReady, ColdRestoreOperationState.ReplayingPrefix })
                Assert.IsTrue(ColdRestoreOperationStateMachine.CanTransition(state, ColdRestoreOperationState.Cancelled), state.ToString());

            foreach (var state in new[] {
                ColdRestoreOperationState.PausedAtTarget, ColdRestoreOperationState.Completed,
                ColdRestoreOperationState.Cancelled, ColdRestoreOperationState.Failed })
                Assert.IsFalse(ColdRestoreOperationStateMachine.CanTransition(state, ColdRestoreOperationState.Cancelled), state.ToString());
        }
    }
}
