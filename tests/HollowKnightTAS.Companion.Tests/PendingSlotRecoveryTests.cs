using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.ReplaySave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class PendingSlotRecoveryTests
    {
        [TestMethod]
        [Timeout(15000)]
        public async Task RealOwnerExitAllowsRecoveryAndConflictBlocksLaunch()
        {
            var root = Path.Combine(Path.GetTempPath(), "hktas-owner-exit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            using var owner = Process.Start(new ProcessStartInfo("powershell.exe")
            {
                Arguments = "-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 60\"",
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            })!;
            try
            {
                var started = new DateTimeOffset(owner.StartTime.ToUniversalTime());
                var records = Path.Combine(root, "HollowKnightTAS", "replay-saves", "v1", "slot-recovery");
                var store = new SlotRecoveryStore(records);
                store.Publish(new SlotRecoveryRecord("cold-process-test", owner.Id, started,
                    0, 1, new byte[] { 1 }, null, new byte[] { 2 }, null));
                var save = Path.Combine(root, "user1.dat");
                File.WriteAllBytes(save, new byte[] { 2 });
                using var gate = GameLaunchGate.Acquire();
                var liveError = Assert.ThrowsExactly<InvalidOperationException>(() => PendingSlotRecovery.RecoverBeforeLaunch(root));
                StringAssert.Contains(liveError.Message, "cold-process-test");
                CollectionAssert.AreEqual(new byte[] { 2 }, File.ReadAllBytes(save));
                owner.Kill(); // Only the child created by this test.
                await new ExactColdRestoreProcessMonitor().WaitForExitAsync(owner.Id, started,
                    TimeSpan.FromSeconds(5), CancellationToken.None);
                File.WriteAllBytes(save, new byte[] { 9 });
                var conflict = Assert.ThrowsExactly<InvalidOperationException>(() => PendingSlotRecovery.RecoverBeforeLaunch(root));
                StringAssert.Contains(conflict.Message, "changed");
                CollectionAssert.AreEqual(new byte[] { 9 }, File.ReadAllBytes(save));
                Assert.AreEqual(1, new SlotRecoveryStore(records).FindPendingOwners().Count);
                File.WriteAllBytes(save, new byte[] { 2 });
                Assert.AreEqual(1, PendingSlotRecovery.RecoverBeforeLaunch(root));
                CollectionAssert.AreEqual(new byte[] { 1 }, File.ReadAllBytes(save));
                Assert.AreEqual(0, new SlotRecoveryStore(records).FindPendingOwners().Count);
            }
            finally
            {
                if (!owner.HasExited) { owner.Kill(); await owner.WaitForExitAsync(); }
                Directory.Delete(root, true);
            }
        }
    }
}
