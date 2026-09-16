using HollowKnightTAS.Core.ReplaySave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.ReplaySave
{
    [TestClass]
    public sealed class SlotRollbackGuardTests
    {
        [TestMethod]
        public void RetryAcceptsOriginalOrInstalledButPreservesUnrelatedChanges()
        {
            var original = new byte[] { 1 };
            var installed = new byte[] { 2 };
            Assert.IsTrue(SlotRollbackGuard.CanRestore(new byte[] { 1 }, original, installed));
            Assert.IsTrue(SlotRollbackGuard.CanRestore(new byte[] { 2 }, original, installed));
            Assert.IsFalse(SlotRollbackGuard.CanRestore(new byte[] { 3 }, original, installed));
            Assert.IsFalse(SlotRollbackGuard.CanRestore(null, original, installed));
            Assert.IsFalse(SlotRollbackGuard.CanRestore(new byte[0], null, installed));
            Assert.IsTrue(SlotRollbackGuard.CanRestore(null, null, installed));
            Assert.IsTrue(SlotRollbackGuard.CanRestore(new byte[0], null, new byte[0]));
        }
    }
}
