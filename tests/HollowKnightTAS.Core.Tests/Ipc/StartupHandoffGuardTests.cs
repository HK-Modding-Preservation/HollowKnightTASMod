using System;
using HollowKnightTAS.Core.Ipc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Ipc
{
    [TestClass]
    public sealed class StartupHandoffGuardTests
    {
        [TestMethod]
        public void RequiresStableTitleAndMatchingPreparation()
        {
            var guard = new StartupHandoffGuard();
            Assert.AreEqual("wait-for-title", guard.Prepare("launch-one", false, false));
            Assert.Throws<InvalidOperationException>(() => guard.Commit("launch-one", false, true));
            Assert.AreEqual("prepared", guard.Prepare("launch-one", false, true));
            Assert.Throws<InvalidOperationException>(() => guard.Commit("launch-two", false, true));
            Assert.Throws<InvalidOperationException>(() => guard.Commit("launch-one", false, false));
            guard.Commit("launch-one", false, true);
            Assert.Throws<InvalidOperationException>(() => guard.Commit("launch-one", false, true));
        }

        [TestMethod]
        public void EnteringGameplayPermanentlyCancelsHandoffEvenAfterReturningToTitle()
        {
            var guard = new StartupHandoffGuard();
            Assert.AreEqual("prepared", guard.Prepare("launch-one", false, true));
            guard.ObserveGameplay(true);
            guard.ObserveGameplay(false);
            Assert.AreEqual("ineligible", guard.Prepare("launch-one", false, true));
            Assert.Throws<InvalidOperationException>(() => guard.Commit("launch-one", false, true));
        }

        [TestMethod]
        public void ControlledLaunchCannotEnterRestartLoop()
        {
            var guard = new StartupHandoffGuard();
            Assert.AreEqual("already-controlled", guard.Prepare("launch-one", true, true));
            Assert.Throws<InvalidOperationException>(() => guard.Commit("launch-one", true, true));
        }
    }
}
