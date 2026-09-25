using HollowKnightTAS.Core.Ipc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Ipc
{
    [TestClass]
    public sealed class StartupActivationPolicyTests
    {
        [TestMethod]
        public void OrdinaryLaunchDoesNotActivateRuntime()
        {
            Assert.IsFalse(StartupActivationPolicy.ShouldStartRuntime(false, null));
            Assert.IsFalse(StartupActivationPolicy.ShouldStartRuntime(false, "0"));
            Assert.IsFalse(StartupActivationPolicy.ShouldStartRuntime(false, "true"));
        }

        [TestMethod]
        public void ProtectedAndExistingControlledLaunchesKeepRuntime()
        {
            Assert.IsTrue(StartupActivationPolicy.ShouldStartRuntime(true, "1"));
            Assert.IsTrue(StartupActivationPolicy.ShouldStartRuntime(true, null));
            Assert.IsTrue(StartupActivationPolicy.ShouldStartRuntime(false, "1"));
        }

        [TestMethod]
        public void StudioOnlyAttemptsExplicitMenuSessions()
        {
            Assert.IsTrue(StartupActivationPolicy.IsManualRequest("manual-startup-123"));
            Assert.IsFalse(StartupActivationPolicy.IsManualRequest("20260925-ordinary"));
            Assert.IsFalse(StartupActivationPolicy.IsManualRequest("full-run-123"));
            Assert.IsFalse(StartupActivationPolicy.IsManualRequest("interactive-123"));
        }

        [TestMethod]
        public void FreshMenuRequestCanRestartAfterEarlierGameplayButCannotCommitOutsideTitle()
        {
            // Manual requests create their guard at the click, not at ordinary process startup.
            var request = new StartupHandoffGuard();
            Assert.AreEqual("prepared", request.Prepare("menu-request", false, true));
            Assert.ThrowsExactly<System.InvalidOperationException>(() =>
                request.Commit("menu-request", false, false));
            request.Commit("menu-request", false, true);
            Assert.ThrowsExactly<System.InvalidOperationException>(() =>
                request.Commit("menu-request", false, true));
        }
    }
}
