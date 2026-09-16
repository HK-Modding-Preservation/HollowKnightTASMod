using System;
using System.Diagnostics;
using System.Threading;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class StartupBootGateTests
    {
        [TestMethod]
        public void NativeHandshakeRequiresAcknowledgementAndDisposalReleasesWait()
        {
            var gate = new StartupBootGate();
            using var ready = EventWaitHandle.OpenExisting("Local\\HKTAS.Boot." + gate.Token + ".Ready");
            using var proceed = EventWaitHandle.OpenExisting("Local\\HKTAS.Boot." + gate.Token + ".Continue");
            try
            {
                Assert.IsFalse(gate.IsWaiting);
                ready.Set();
                Assert.IsTrue(gate.IsWaiting);
                Assert.IsFalse(proceed.WaitOne(0));
                var start = new ProcessStartInfo();
                gate.ConfigureInjector(start);
                Assert.AreEqual(gate.Token, start.Environment["HKTAS_BOOT_GATE_TOKEN"]);
                Assert.AreEqual(Environment.ProcessId.ToString(), start.Environment["HKTAS_BOOT_GATE_OWNER"]);
            }
            finally { gate.Dispose(); }
            Assert.IsTrue(proceed.WaitOne(0), "Closing Studio must release an already-open native wait.");
            Assert.IsFalse(gate.IsWaiting);
        }

        [TestMethod]
        public void ContinueDoesNotReleaseAnotherLaunch()
        {
            using var first = new StartupBootGate();
            using var second = new StartupBootGate();
            using var firstReady = EventWaitHandle.OpenExisting("Local\\HKTAS.Boot." + first.Token + ".Ready");
            using var secondReady = EventWaitHandle.OpenExisting("Local\\HKTAS.Boot." + second.Token + ".Ready");
            firstReady.Set(); secondReady.Set();
            first.Continue();
            Assert.IsFalse(first.IsWaiting);
            Assert.IsTrue(second.IsWaiting);
        }
    }
}
