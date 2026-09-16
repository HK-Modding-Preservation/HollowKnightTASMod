using System;
using HollowKnightTAS.Core.ReplaySave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.ReplaySave
{
    [TestClass]
    public sealed class AutoSaveRestoredBoundaryTests
    {
        [TestMethod]
        public void EnableAfterRestoreCountsFromCurrentBoundaryWithoutCatchupBurst()
        {
            var scheduler = new ReplaySaveScheduler(new AutoSavePolicy(false));
            Assert.IsTrue(scheduler.SetPolicy(new AutoSavePolicy(true, 3, 20), 90).Success);
            Assert.AreEqual(93L, scheduler.NextAutomaticMovieTick);
            Assert.IsNull(scheduler.OnMovieTickCommitted(91, DateTimeOffset.UtcNow));
            Assert.IsNull(scheduler.OnMovieTickCommitted(92, DateTimeOffset.UtcNow));
            Assert.IsNotNull(scheduler.OnMovieTickCommitted(93, DateTimeOffset.UtcNow));
            Assert.AreEqual(96L, scheduler.NextAutomaticMovieTick);
            Assert.IsNull(scheduler.OnMovieTickCommitted(94, DateTimeOffset.UtcNow));
            Assert.IsFalse(scheduler.SetPolicy(new AutoSavePolicy(false), 90).Success);
            Assert.IsTrue(scheduler.Policy.Enabled);
        }
    }
}
