using System;
using System.IO;
using System.Threading.Tasks;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class GameLaunchGateTests
    {
        [TestMethod]
        public async Task GateRejectsCompetingLaunchAcrossAsyncBoundaryAndCanBeReacquired()
        {
            var directory = Path.Combine(Path.GetTempPath(), "hktas-launch-gate-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var first = GameLaunchGate.Acquire(directory))
                {
                    await Task.Run(() => Assert.ThrowsExactly<InvalidOperationException>(
                        () => GameLaunchGate.Acquire(directory)));
                    Assert.AreEqual(0L, new FileInfo(Path.Combine(directory, "game-launch.lock")).Length);
                }
                using var second = GameLaunchGate.Acquire(directory);
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestMethod]
        public void FailedLaunchScopeReleasesGateWithoutDeletingItsIdentity()
        {
            var directory = Path.Combine(Path.GetTempPath(), "hktas-launch-gate-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.ThrowsExactly<InvalidDataException>(() =>
                {
                    using var gate = GameLaunchGate.Acquire(directory);
                    throw new InvalidDataException("launch failed");
                });
                Assert.IsTrue(File.Exists(Path.Combine(directory, "game-launch.lock")));
                using var retry = GameLaunchGate.Acquire(directory);
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
