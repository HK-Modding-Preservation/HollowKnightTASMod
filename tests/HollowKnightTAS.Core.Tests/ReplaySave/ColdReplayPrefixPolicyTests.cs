using System;
using HollowKnightTAS.Core.ReplaySave;
using HollowKnightTAS.Runtime.ReplaySave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.ReplaySave
{
    [TestClass]
    public sealed class ColdReplayPrefixPolicyTests
    {
        [TestMethod]
        public void SavedStateRequiresFullPrefixButDerivedSeekOnlyRequiresBaseline()
        {
            Assert.AreEqual(6L, ColdReplayPrefixPolicy.RequiredTicks(ColdRestoreOperationKind.RestoreReplaySave, 5));
            Assert.AreEqual(0L, ColdReplayPrefixPolicy.RequiredTicks(ColdRestoreOperationKind.SeekMovieTick, 5));
            Assert.AreEqual(0L, ColdReplayPrefixPolicy.RequiredTicks(ColdRestoreOperationKind.ApplyBranchAndSeek, 5));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ColdReplayPrefixPolicy.RequiredTicks((ColdRestoreOperationKind)0, 5));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ColdReplayPrefixPolicy.RequiredTicks(ColdRestoreOperationKind.SeekMovieTick, -1));
        }
    }
}
