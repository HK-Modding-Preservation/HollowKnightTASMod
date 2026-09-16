using System;
using System.Linq;
using HollowKnightTAS.Core.ReplaySave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.ReplaySave
{
    [TestClass]
    public sealed class ReplaySaveSchedulerTests
    {
        [TestMethod]
        public void AutomaticIntervalUsesCommittedMovieTicksAndDoesNotDuplicatePause()
        {
            var scheduler = new ReplaySaveScheduler(
                new AutoSavePolicy(true, 120, 3));
            for (var tick = 0; tick < 1000; tick++)
            {
                scheduler.OnMovieTickCommitted(
                    tick,
                    ReplaySaveTestFactory.RequestedAt.AddSeconds(tick));
            }

            Assert.HasCount(8, scheduler.Pending);
            CollectionAssert.AreEqual(
                new long[] { 119, 239, 359, 479, 599, 719, 839, 959 },
                scheduler.Pending
                    .Select(value => value.RequestedAtMovieTick)
                    .ToArray());
            Assert.AreEqual(1079, scheduler.NextAutomaticMovieTick);
            Assert.ThrowsExactly<InvalidOperationException>(
                () => scheduler.OnMovieTickCommitted(
                    999,
                    ReplaySaveTestFactory.RequestedAt));
            Assert.HasCount(8, scheduler.Pending);
        }

        [TestMethod]
        public void ArbitraryRequestWaitsForSafeTickAndPreservesRequestedTick()
        {
            var scheduler = new ReplaySaveScheduler(
                new AutoSavePolicy(false, 120, 3));
            scheduler.OnMovieTickCommitted(
                20,
                ReplaySaveTestFactory.RequestedAt);
            var result = scheduler.Request(
                "during transition",
                ReplaySaveReason.Manual,
                ReplaySaveTestFactory.RequestedAt.AddSeconds(1),
                requireSubsequentCommittedTick: true);

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(ReplaySaveStatus.Pending, result.Status);
            Assert.IsFalse(
                scheduler.TryDequeueForSafeTick(20, out _));
            Assert.IsTrue(
                scheduler.TryDequeueForSafeTick(21, out var request));
            Assert.IsNotNull(request);
            Assert.AreEqual(20, request.RequestedAtMovieTick);
            Assert.AreEqual(21, request.MinimumEffectiveMovieTick);
            Assert.IsTrue(request.RequireSubsequentCommittedTick);
            Assert.AreEqual("during transition", request.Label);
        }

        [TestMethod]
        public void ShutdownFailsEveryPendingRequestInsteadOfInventingSuccess()
        {
            var scheduler = new ReplaySaveScheduler(AutoSavePolicy.Default);
            scheduler.Request(
                "one",
                ReplaySaveReason.Manual,
                ReplaySaveTestFactory.RequestedAt);
            scheduler.Request(
                "two",
                ReplaySaveReason.MovieCheckpointCommand,
                ReplaySaveTestFactory.RequestedAt);

            Assert.HasCount(2, scheduler.FailPendingOnShutdown());
            Assert.AreEqual(0, scheduler.PendingCount);
        }
    }
}
