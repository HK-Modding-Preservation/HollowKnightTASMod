using System;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class RecordingSessionRestartCoordinatorTests
    {
        [TestMethod]
        public async Task CancellingCommittedExitWaitsForExitAndDoesNotLaunch()
        {
            var host = new Host();
            var coordinator = new RecordingSessionRestartCoordinator(host);
            var id = coordinator.Begin(4);
            Assert.AreEqual(RecordingRestartPhase.ExitingSource, coordinator.Latest!.Phase);
            Assert.ThrowsExactly<InvalidOperationException>(() => coordinator.Begin(4));
            var cancel = coordinator.CancelAsync(id);
            Assert.IsFalse(cancel.IsCompleted);
            host.Exited.SetResult(true);
            Assert.AreEqual(RecordingRestartPhase.Cancelled, (await cancel).Phase);
            Assert.AreEqual(0, host.Launches);
            Assert.IsFalse(coordinator.IsActive);
        }

        [TestMethod]
        public async Task ReadyRequiresVerifiedOriginAndRejectsLateCancellation()
        {
            var host = new Host();
            host.Exited.SetResult(true);
            var coordinator = new RecordingSessionRestartCoordinator(host);
            var id = coordinator.Begin(4);
            Assert.AreEqual(RecordingRestartPhase.WaitingForOrigin, coordinator.Latest!.Phase);
            Assert.IsFalse(host.Released);
            host.Origin.SetResult(true);
            await coordinator.WaitAsync(id);
            Assert.AreEqual(RecordingRestartPhase.Ready, coordinator.Latest.Phase);
            Assert.IsTrue(host.Released);
            Assert.IsFalse(host.Killed);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => coordinator.CancelAsync(id));
        }

        [TestMethod]
        public async Task OriginFailureCleansOwnedTargetAndDoesNotRetry()
        {
            var host = new Host();
            host.Exited.SetResult(true);
            var coordinator = new RecordingSessionRestartCoordinator(host);
            var id = coordinator.Begin(4);
            host.Origin.SetException(new InvalidOperationException("origin failed"));
            await coordinator.WaitAsync(id);
            Assert.AreEqual(RecordingRestartPhase.Failed, coordinator.Latest!.Phase);
            StringAssert.Contains(coordinator.Latest.Detail, "origin failed");
            Assert.AreEqual(1, host.Launches);
            Assert.IsTrue(host.Killed);
            Assert.IsFalse(coordinator.IsActive);
        }

        private sealed class Host : IRecordingRestartHost, IPreparedRecordingRestart, IRecordingRestartTarget
        {
            public TaskCompletionSource<bool> Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Origin { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int Launches;
            public bool Released;
            public bool Killed;
            public Task<IPreparedRecordingRestart> PrepareAsync(int slot, CancellationToken token) => Task.FromResult<IPreparedRecordingRestart>(this);
            public Task ExitSourceAsync(CancellationToken token) => Exited.Task.WaitAsync(token);
            public Task<IRecordingRestartTarget> LaunchAsync(string id, CancellationToken token)
            { Launches++; return Task.FromResult<IRecordingRestartTarget>(this); }
            public Task WaitForRecordingOriginAsync(int slot, CancellationToken token) => Origin.Task.WaitAsync(token);
            public void ReleaseSupervision() => Released = true;
            public void Dispose() { if (Launches != 0 && !Released) Killed = true; }
        }
    }
}
