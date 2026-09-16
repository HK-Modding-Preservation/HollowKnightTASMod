using System.Collections.Generic;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Rng;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Rng
{
    [TestClass]
    public sealed class RngDiffTests
    {
        [TestMethod]
        public void MatchingTrace_IsMatch()
        {
            var trace = Trace(StateHash('1'), CallAfterHash('2'));

            var result = RngDiff.Compare(trace, trace);

            Assert.AreEqual(RngComparisonStatus.Match, result.Status);
            Assert.IsTrue(result.IsMatch);
        }

        [TestMethod]
        public void StateDivergence_ReportsFirstMovieTick()
        {
            var expected = Trace(StateHash('1'), CallAfterHash('2'));
            var actual = Trace(StateHash('3'), CallAfterHash('2'));

            var result = RngDiff.Compare(expected, actual);

            Assert.AreEqual(
                RngComparisonStatus.Diverged,
                result.Status);
            Assert.AreEqual(10, result.FirstMovieTick);
            StringAssert.Contains(result.Reason, "state");
            Assert.IsNotNull(result.ExpectedState);
            Assert.IsNotNull(result.ActualState);
        }

        [TestMethod]
        public void CallDivergence_ProvidesCallSiteAndStateContext()
        {
            var expected = Trace(StateHash('1'), CallAfterHash('2'));
            var actual = Trace(StateHash('1'), CallAfterHash('4'));

            var result = RngDiff.Compare(expected, actual);

            Assert.AreEqual(
                RngComparisonStatus.Diverged,
                result.Status);
            Assert.AreEqual(12, result.FirstMovieTick);
            Assert.AreEqual(
                "helper.random-vector2",
                result.ExpectedCall!.CallSiteId);
            Assert.AreEqual(
                CallAfterHash('4'),
                result.ActualCall!.AfterStateSha256);
        }

        [TestMethod]
        public void CodecMismatch_IsIncomparable()
        {
            var expected = Trace(StateHash('1'), CallAfterHash('2'));
            var actual = new RngTrace(
                "other-codec",
                RngWhitelist.CoverageId,
                expected.States,
                expected.Calls);

            var result = RngDiff.Compare(expected, actual);

            Assert.AreEqual(
                RngComparisonStatus.Incomparable,
                result.Status);
        }

        private static RngTrace Trace(
            string stateHash,
            string callAfterHash)
        {
            var stamp = new TickStamp(
                100,
                200,
                300,
                0,
                TickPhase.InControlCommitted);
            var counts = new Dictionary<string, long>
            {
                ["helper.random-vector2"] = 1,
                ["hero.take-damage"] = 0
            };
            var state = new RngStateRecord(
                10,
                stamp,
                stateHash,
                1,
                counts);
            var call = new RngCallRecord(
                12,
                stamp,
                "helper.random-vector2",
                1,
                1,
                StateHash('a'),
                callAfterHash);
            return new RngTrace(
                "codec-v1",
                RngWhitelist.CoverageId,
                new[] { state },
                new[] { call });
        }

        private static string StateHash(char value)
        {
            return new string(value, 64);
        }

        private static string CallAfterHash(char value)
        {
            return new string(value, 64);
        }
    }
}
