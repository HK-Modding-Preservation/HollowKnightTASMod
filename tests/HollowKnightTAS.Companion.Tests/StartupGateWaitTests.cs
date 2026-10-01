using System;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests;

[TestClass]
public sealed class StartupGateWaitTests
{
    [TestMethod]
    public async Task DelayedReadyMustAlsoHaveAnArmedSaveGuard()
    {
        using var gate = new StartupBootGate(frameBased: true, fullRun: true);
        using var map = MemoryMappedFile.OpenExisting("Local\\HKTAS.Boot." + gate.Token + ".V2State");
        using var state = map.CreateViewAccessor();
        using var ready = EventWaitHandle.OpenExisting("Local\\HKTAS.Boot." + gate.Token + ".Ready");
        var wait = gate.WaitForStartupAsync(() => false, TimeSpan.FromSeconds(3), CancellationToken.None);
        ready.Set();
        await Task.Delay(120);
        Assert.IsFalse(wait.IsCompleted, "A Ready event alone must not accept an unprotected game.");
        state.Write(92, 1);
        await wait;
        Assert.AreEqual(0L, gate.NativeCompletedFrames);
    }

    [TestMethod]
    public async Task NativeFaultAndEarlyExitAreNotReportedAsUnsupportedBridge()
    {
        using var gate = new StartupBootGate(frameBased: true, fullRun: true);
        var exited = await Assert.ThrowsAsync<InvalidOperationException>(() => gate.WaitForStartupAsync(
            () => true, TimeSpan.FromSeconds(60), CancellationToken.None));
        StringAssert.Contains(exited.Message, "已退出");
        using var map = MemoryMappedFile.OpenExisting("Local\\HKTAS.Boot." + gate.Token + ".V2State");
        using var state = map.CreateViewAccessor();
        state.Write(88, 61);
        var fault = await Assert.ThrowsAsync<InvalidOperationException>(() => gate.WaitForStartupAsync(
            () => false, TimeSpan.FromSeconds(60), CancellationToken.None));
        StringAssert.Contains(fault.Message, "61");
    }

    [TestMethod]
    public async Task TimeoutIncludesStateAndCancellationRemainsPrompt()
    {
        using var gate = new StartupBootGate(frameBased: true, fullRun: true);
        var timeout = await Assert.ThrowsAsync<TimeoutException>(() => gate.WaitForStartupAsync(
            () => false, TimeSpan.FromMilliseconds(70), CancellationToken.None));
        StringAssert.Contains(timeout.Message, "原生帧=0");
        StringAssert.Contains(timeout.Message, "存档保护=0");
        using var cancel = new CancellationTokenSource();
        var wait = gate.WaitForStartupAsync(() => false, TimeSpan.FromSeconds(60), cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => wait);
    }
}
