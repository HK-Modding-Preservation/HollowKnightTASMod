using System;
using System.Threading;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class StartupBootControllerTests
    {
        [TestMethod]
        public void ContinueRequiresNativeAcknowledgementAndAllowsNextLaunch()
        {
            using var controller = new StartupBootController();
            var gate = controller.Begin();
            Assert.IsTrue(controller.IsPending);
            Assert.IsFalse(controller.IsWaiting);
            Assert.Throws<InvalidOperationException>(() => controller.Continue());
            Assert.Throws<InvalidOperationException>(() => controller.Begin());
            using var ready = EventWaitHandle.OpenExisting("Local\\HKTAS.Boot." + gate.Token + ".Ready");
            using var proceed = EventWaitHandle.OpenExisting("Local\\HKTAS.Boot." + gate.Token + ".Continue");
            ready.Set();
            controller.Refresh();
            Assert.IsTrue(controller.IsWaiting);
            controller.Continue();
            Assert.IsTrue(gate.IsAcknowledged, "Continue before the injector receipt must preserve the acknowledgement.");
            Assert.IsTrue(proceed.WaitOne(0));
            Assert.IsFalse(controller.IsPending);
            Assert.IsFalse(controller.IsWaiting);
            Assert.AreNotEqual(gate.Token, controller.Begin().Token);
        }

        [TestMethod]
        public void ClosingControllerReleasesNativeWaitAndNotifiesUi()
        {
            using var controller = new StartupBootController();
            var changes = 0;
            controller.Changed += (_, _) => changes++;
            var gate = controller.Begin();
            using var proceed = EventWaitHandle.OpenExisting("Local\\HKTAS.Boot." + gate.Token + ".Continue");
            controller.Dispose();
            Assert.IsTrue(proceed.WaitOne(0));
            Assert.AreEqual(2, changes);
            Assert.IsFalse(controller.IsPending);
        }
    }
}
