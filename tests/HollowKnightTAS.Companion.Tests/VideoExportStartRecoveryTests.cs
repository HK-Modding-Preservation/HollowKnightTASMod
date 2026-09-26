using System;
using System.Threading.Tasks;
using HollowKnightTAS.Companion.Automation;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class VideoExportStartRecoveryTests
    {
        [TestMethod]
        public async Task SuccessfulStartKeepsCapture()
        {
            var calls = 0;
            var result = await VideoExportStartRecovery.RunAsync(
                () => Task.FromResult(new NativeFrameBoundary(12, 3, "Running", "")),
                () => false, _ => { calls++; return Task.CompletedTask; });
            Assert.AreEqual("Running", result.Mode);
            Assert.AreEqual(0, calls);
        }

        [TestMethod]
        public async Task CancelledStartUsesFreshTokenToCancelAcceptedCapture()
        {
            var calls = 0;
            var result = await VideoExportStartRecovery.RunAsync(
                () => throw new OperationCanceledException("client cancelled"), () => false,
                token => { Assert.IsFalse(token.IsCancellationRequested); calls++; return Task.CompletedTask; });
            Assert.AreEqual("Fault", result.Mode);
            Assert.AreEqual(1, calls);
            StringAssert.Contains(result.Error, "client cancelled");
            StringAssert.Contains(result.Error, "capture was cancelled");
        }

        [TestMethod]
        public async Task TimeoutFaultWithLiveNativeGateStillCancelsCapture()
        {
            var calls = 0;
            var result = await VideoExportStartRecovery.RunAsync(
                () => Task.FromResult(new NativeFrameBoundary(12, 3, "Fault", "command timed out")),
                () => false, _ => { calls++; return Task.CompletedTask; });
            Assert.AreEqual(1, calls);
            Assert.AreEqual(12L, result.CompletedFrame);
            StringAssert.Contains(result.Error, "command timed out");
        }

        [TestMethod]
        public async Task NativeFaultDoesNotQueueUnserviceableUnityWork()
        {
            var result = await VideoExportStartRecovery.RunAsync(
                () => Task.FromResult(new NativeFrameBoundary(12, 3, "Fault", "Native fault 41")),
                () => true, _ => throw new AssertFailedException("Faulted native gate cannot service cancellation."));
            Assert.AreEqual("Fault", result.Mode);
            StringAssert.Contains(result.Error, "main-thread cancellation is unavailable");
        }

        [TestMethod]
        public async Task FailedCompensationRetainsOriginalFailureAndReportsUnknownCleanup()
        {
            var result = await VideoExportStartRecovery.RunAsync(
                () => throw new InvalidOperationException("playback failed"), () => false,
                _ => throw new InvalidOperationException("runtime disconnected"));
            StringAssert.Contains(result.Error, "playback failed");
            StringAssert.Contains(result.Error, "runtime disconnected");
            StringAssert.Contains(result.Error, "could not be confirmed");
        }
    }
}
