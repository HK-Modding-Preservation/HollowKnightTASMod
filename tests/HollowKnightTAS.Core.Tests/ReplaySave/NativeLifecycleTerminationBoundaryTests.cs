using HollowKnightTAS.Core.ReplaySave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.ReplaySave
{
    [TestClass]
    public sealed class NativeLifecycleTerminationBoundaryTests
    {
        [TestMethod]
        public void SourceExitIsOfferedOnlyAfterFailedLoadDeadline()
        {
            Assert.IsFalse(NativeLifecycleTerminationBoundary.CanExitFailedSource(false, 300));
            Assert.IsFalse(NativeLifecycleTerminationBoundary.CanExitFailedSource(true, 119.99));
            Assert.IsFalse(NativeLifecycleTerminationBoundary.CanExitFailedSource(true, double.NaN));
            Assert.IsTrue(NativeLifecycleTerminationBoundary.CanExitFailedSource(true, 120));
        }

        [TestMethod]
        public void FailedBeforeNativeStartCanDrainOnlyAtAStableBoundary()
        {
            foreach (var loadingSlot in new[] { false, true })
            {
                Assert.IsTrue(NativeLifecycleTerminationBoundary.CanPause(false, false, loadingSlot, false, 0, 3, true, false));
                Assert.IsTrue(NativeLifecycleTerminationBoundary.CanPause(false, false, loadingSlot, false, 0, 3, false, true));
                Assert.IsFalse(NativeLifecycleTerminationBoundary.CanPause(false, false, loadingSlot, false, 0, 3, false, false));
                Assert.IsFalse(NativeLifecycleTerminationBoundary.CanPause(true, false, loadingSlot, false, 0, 3, true, false));
            }
        }

        [TestMethod]
        public void CleanupWaitsForNativeCompletionAndTheActualDestination()
        {
            Assert.IsFalse(NativeLifecycleTerminationBoundary.CanPause(true, false, true, false, 0, 2, true, false));
            Assert.IsFalse(NativeLifecycleTerminationBoundary.CanPause(true, true, true, true, 1, 2, true, false));
            Assert.IsTrue(NativeLifecycleTerminationBoundary.CanPause(true, true, true, true, 1, 2, false, true));
            Assert.IsFalse(NativeLifecycleTerminationBoundary.CanPause(true, true, true, false, 2, 2, true, false));
            Assert.IsTrue(NativeLifecycleTerminationBoundary.CanPause(true, true, true, false, 2, 3, true, false));
            Assert.IsFalse(NativeLifecycleTerminationBoundary.CanPause(true, false, false, false, 0, 3, true, false));
            Assert.IsTrue(NativeLifecycleTerminationBoundary.CanPause(true, true, false, false, 0, 3, true, false));
            Assert.IsTrue(NativeLifecycleTerminationBoundary.CanPause(false, false, true, false, 0, 3, false, true));
        }
    }
}
