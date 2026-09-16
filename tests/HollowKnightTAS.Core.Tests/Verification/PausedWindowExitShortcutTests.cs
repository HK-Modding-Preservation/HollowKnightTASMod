using HollowKnightTAS.Runtime.Ipc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Verification
{
    [TestClass]
    public sealed class PausedWindowExitShortcutTests
    {
        [TestMethod]
        public void RequiresBothHeldKeysInUnchangedOwnedForegroundWindow()
        {
            Assert.IsTrue(PausedWindowExitShortcut.Matches(7, 7, short.MinValue, short.MinValue, true));
            Assert.IsTrue(PausedWindowExitShortcut.Matches(7, 7, -1, -1, true));
            Assert.IsFalse(PausedWindowExitShortcut.Matches(0, 0, -1, -1, true));
            Assert.IsFalse(PausedWindowExitShortcut.Matches(8, 7, -1, -1, true));
            Assert.IsFalse(PausedWindowExitShortcut.Matches(7, 7, -1, -1, false));
            Assert.IsFalse(PausedWindowExitShortcut.Matches(7, 7, 0, -1, true));
            Assert.IsFalse(PausedWindowExitShortcut.Matches(7, 7, -1, 0, true));
            Assert.IsFalse(PausedWindowExitShortcut.Matches(7, 7, 1, 1, true));
            Assert.IsFalse(PausedWindowExitShortcut.Matches(7, 7, 1, -1, true));
            Assert.IsFalse(PausedWindowExitShortcut.Matches(7, 7, -1, 1, true));
        }
    }
}
