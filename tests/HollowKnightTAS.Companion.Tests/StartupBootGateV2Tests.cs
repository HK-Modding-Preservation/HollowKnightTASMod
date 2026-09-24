using System;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class StartupBootGateV2Tests
    {
        [TestMethod]
        public async Task ArmIsFrameZeroOnly_AndStepCarriesExpectedFrameAndAck()
        {
            using var gate = new StartupBootGate(frameBased: true, fullRun: true);
            using var mapping = MemoryMappedFile.OpenExisting(
                "Local\\HKTAS.Boot." + gate.Token + ".V2State");
            using var view = mapping.CreateViewAccessor();
            using var ready = EventWaitHandle.OpenExisting(
                "Local\\HKTAS.Boot." + gate.Token + ".Ready");
            using var command = EventWaitHandle.OpenExisting(
                "Local\\HKTAS.Boot." + gate.Token + ".V2Command");
            Assert.Throws<InvalidOperationException>(() =>
                gate.ArmV2(gate.Token, new string('a', 64)));
            view.Write(92, 1);
            ready.Set();
            Assert.IsTrue(gate.IsWaiting);
            Assert.Throws<InvalidOperationException>(() =>
                gate.ArmV2(new string('b', 32), new string('a', 64)));
            gate.ArmV2(gate.Token, new string('a', 64));
            Assert.AreEqual(1, view.ReadInt32(80));
            Assert.Throws<InvalidOperationException>(() =>
                gate.ArmV2(gate.Token, new string('a', 64)));

            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var fakeNative = Task.Run(() =>
            {
                for (var frame = 0; frame < 2; frame++)
                {
                    Assert.IsTrue(command.WaitOne(TimeSpan.FromSeconds(3)));
                    Assert.AreEqual((long)frame, view.ReadInt64(64));
                    Assert.AreEqual(1, view.ReadInt32(72));
                    Assert.AreEqual((long)frame + 1, view.ReadInt64(48));
                    ready.Reset();
                    view.Write(76, 1);
                    view.Write(56, (long)frame + 1);
                    view.Write(40, (long)frame + 1);
                    view.Write(76, 0);
                    ready.Set();
                }
            }, cancel.Token);
            var first = await gate.StepV2Async(0, cancel.Token);
            Assert.AreEqual(1L, first.CompletedFrame);
            Assert.AreEqual(1L, first.AckSequence);
            Assert.AreEqual("Paused", first.Mode);
            var second = await gate.StepV2Async(1, cancel.Token);
            Assert.AreEqual(2L, second.CompletedFrame);
            Assert.AreEqual(2L, second.AckSequence);
            await fakeNative;
            Assert.Throws<InvalidOperationException>(() =>
                gate.StepV2Async(1, cancel.Token).GetAwaiter().GetResult());
        }
    }
}
