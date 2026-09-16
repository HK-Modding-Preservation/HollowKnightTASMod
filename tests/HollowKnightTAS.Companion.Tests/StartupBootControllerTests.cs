using System;
using System.Threading;
using System.IO.MemoryMappedFiles;
using System.Diagnostics;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class StartupBootControllerTests
    {
        [TestMethod]
        public void FrameStepWaitsForNewCompletedBoundaryAndPreservesAcknowledgement()
        {
            using var controller = new StartupBootController();
            var gate = controller.Begin();
            using var ready = EventWaitHandle.OpenExisting("Local\\HKTAS.Boot." + gate.Token + ".Ready");
            using var step = EventWaitHandle.OpenExisting("Local\\HKTAS.Boot." + gate.Token + ".Step");
            using var mapping = MemoryMappedFile.OpenExisting("Local\\HKTAS.Boot." + gate.Token + ".State");
            using var view = mapping.CreateViewAccessor();
            var start = new ProcessStartInfo();
            gate.ConfigureInjector(start);
            Assert.AreEqual("1", start.Environment["HKTAS_BOOT_FRAME_GATE"]);
            view.Write(12, 1);
            view.Write(4, 1);
            ready.Set();
            controller.Refresh();
            Assert.IsTrue(controller.CanStep);
            Assert.AreEqual(0, controller.CompletedFrames);
            controller.Step();
            Assert.IsTrue(step.WaitOne(0));
            Assert.IsFalse(step.WaitOne(0), "Each request grants exactly one native token.");
            Assert.IsTrue(gate.IsAcknowledged, "Acknowledgement must survive the transient stepping state.");
            Assert.IsFalse(controller.CanStep);
            Assert.Throws<InvalidOperationException>(() => controller.Step());
            ready.Set(); // A stale ready indication alone cannot complete a step.
            controller.Refresh();
            Assert.IsFalse(controller.CanStep);
            view.Write(0, 1);
            controller.Refresh();
            Assert.IsTrue(controller.CanStep);
            Assert.AreEqual(1, controller.CompletedFrames);
            controller.Continue();
            Assert.IsFalse(controller.IsPending);
        }

        [TestMethod]
        public void ContinueRequiresNativeAcknowledgementAndAllowsNextLaunch()
        {
            using var controller = new StartupBootController();
            var gate = controller.Begin(frameBased: false);
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
            var gate = controller.Begin(frameBased: false);
            using var proceed = EventWaitHandle.OpenExisting("Local\\HKTAS.Boot." + gate.Token + ".Continue");
            controller.Dispose();
            Assert.IsTrue(proceed.WaitOne(0));
            Assert.AreEqual(2, changes);
            Assert.IsFalse(controller.IsPending);
        }
    }
}
