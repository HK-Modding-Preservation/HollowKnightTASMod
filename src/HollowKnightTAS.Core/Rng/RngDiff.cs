using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace HollowKnightTAS.Core.Rng
{
    public sealed class RngTrace
    {
        private readonly ReadOnlyCollection<RngStateRecord> states;
        private readonly ReadOnlyCollection<RngCallRecord> calls;

        public RngTrace(
            string codecId,
            string coverageId,
            IEnumerable<RngStateRecord> states,
            IEnumerable<RngCallRecord> calls)
        {
            CodecId = RngValidation.RequireIdentifier(
                codecId,
                nameof(codecId));
            CoverageId = RngValidation.RequireIdentifier(
                coverageId,
                nameof(coverageId));
            if (states == null)
            {
                throw new ArgumentNullException(nameof(states));
            }

            if (calls == null)
            {
                throw new ArgumentNullException(nameof(calls));
            }

            this.states = new ReadOnlyCollection<RngStateRecord>(
                states.ToList());
            this.calls = new ReadOnlyCollection<RngCallRecord>(
                calls.ToList());
        }

        public string CodecId { get; }
        public string CoverageId { get; }
        public IReadOnlyList<RngStateRecord> States => states;
        public IReadOnlyList<RngCallRecord> Calls => calls;
    }

    public enum RngComparisonStatus : byte
    {
        Match = 1,
        Diverged = 2,
        Incomparable = 3
    }

    public sealed class RngDifference
    {
        internal RngDifference(
            RngComparisonStatus status,
            string reason,
            long firstMovieTick,
            RngStateRecord? expectedState,
            RngStateRecord? actualState,
            RngCallRecord? expectedCall,
            RngCallRecord? actualCall)
        {
            Status = status;
            Reason = reason ?? string.Empty;
            FirstMovieTick = firstMovieTick;
            ExpectedState = expectedState;
            ActualState = actualState;
            ExpectedCall = expectedCall;
            ActualCall = actualCall;
        }

        public RngComparisonStatus Status { get; }
        public string Reason { get; }
        public long FirstMovieTick { get; }
        public RngStateRecord? ExpectedState { get; }
        public RngStateRecord? ActualState { get; }
        public RngCallRecord? ExpectedCall { get; }
        public RngCallRecord? ActualCall { get; }
        public bool IsMatch => Status == RngComparisonStatus.Match;
    }

    public static class RngDiff
    {
        public static RngDifference Compare(
            RngTrace expected,
            RngTrace actual)
        {
            if (expected == null)
            {
                throw new ArgumentNullException(nameof(expected));
            }

            if (actual == null)
            {
                throw new ArgumentNullException(nameof(actual));
            }

            if (!string.Equals(
                    expected.CodecId,
                    actual.CodecId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    expected.CoverageId,
                    actual.CoverageId,
                    StringComparison.Ordinal))
            {
                return new RngDifference(
                    RngComparisonStatus.Incomparable,
                    "RNG codec or coverage differs.",
                    -1,
                    null,
                    null,
                    null,
                    null);
            }

            var stateDifference = FirstStateDifference(
                expected.States,
                actual.States);
            var callDifference = FirstCallDifference(
                expected.Calls,
                actual.Calls);
            if (stateDifference == null && callDifference == null)
            {
                return new RngDifference(
                    RngComparisonStatus.Match,
                    "RNG states and whitelisted call records match.",
                    -1,
                    null,
                    null,
                    null,
                    null);
            }

            if (stateDifference != null
                && (callDifference == null
                    || stateDifference.MovieTick
                    <= callDifference.MovieTick))
            {
                return new RngDifference(
                    RngComparisonStatus.Diverged,
                    stateDifference.Reason,
                    stateDifference.MovieTick,
                    stateDifference.Expected,
                    stateDifference.Actual,
                    null,
                    null);
            }

            return new RngDifference(
                RngComparisonStatus.Diverged,
                callDifference!.Reason,
                callDifference.MovieTick,
                null,
                null,
                callDifference.Expected,
                callDifference.Actual);
        }

        private static StateDifference? FirstStateDifference(
            IReadOnlyList<RngStateRecord> expected,
            IReadOnlyList<RngStateRecord> actual)
        {
            var count = Math.Min(expected.Count, actual.Count);
            for (var index = 0; index < count; index++)
            {
                var left = expected[index];
                var right = actual[index];
                if (left.MovieTick != right.MovieTick
                    || !string.Equals(
                        left.StateSha256,
                        right.StateSha256,
                        StringComparison.Ordinal)
                    || left.GlobalCallCount != right.GlobalCallCount
                    || !CountsEqual(
                        left.CallSiteCounts,
                        right.CallSiteCounts))
                {
                    return new StateDifference(
                        Math.Min(left.MovieTick, right.MovieTick),
                        "First RNG state/counter difference.",
                        left,
                        right);
                }
            }

            if (expected.Count == actual.Count)
            {
                return null;
            }

            var extra = expected.Count > count
                ? expected[count]
                : actual[count];
            return new StateDifference(
                extra.MovieTick,
                "RNG state record count differs.",
                expected.Count > count ? expected[count] : null,
                actual.Count > count ? actual[count] : null);
        }

        private static CallDifference? FirstCallDifference(
            IReadOnlyList<RngCallRecord> expected,
            IReadOnlyList<RngCallRecord> actual)
        {
            var count = Math.Min(expected.Count, actual.Count);
            for (var index = 0; index < count; index++)
            {
                var left = expected[index];
                var right = actual[index];
                if (left.MovieTick != right.MovieTick
                    || !string.Equals(
                        left.CallSiteId,
                        right.CallSiteId,
                        StringComparison.Ordinal)
                    || left.GlobalCallIndex != right.GlobalCallIndex
                    || left.CallSiteIndex != right.CallSiteIndex
                    || !string.Equals(
                        left.BeforeStateSha256,
                        right.BeforeStateSha256,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        left.AfterStateSha256,
                        right.AfterStateSha256,
                        StringComparison.Ordinal))
                {
                    return new CallDifference(
                        Math.Min(left.MovieTick, right.MovieTick),
                        "First whitelisted RNG call difference.",
                        left,
                        right);
                }
            }

            if (expected.Count == actual.Count)
            {
                return null;
            }

            var extra = expected.Count > count
                ? expected[count]
                : actual[count];
            return new CallDifference(
                extra.MovieTick,
                "Whitelisted RNG call count differs.",
                expected.Count > count ? expected[count] : null,
                actual.Count > count ? actual[count] : null);
        }

        private static bool CountsEqual(
            IReadOnlyDictionary<string, long> expected,
            IReadOnlyDictionary<string, long> actual)
        {
            if (expected.Count != actual.Count)
            {
                return false;
            }

            foreach (var item in expected)
            {
                if (!actual.TryGetValue(item.Key, out var value)
                    || value != item.Value)
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class StateDifference
        {
            public StateDifference(
                long movieTick,
                string reason,
                RngStateRecord? expected,
                RngStateRecord? actual)
            {
                MovieTick = movieTick;
                Reason = reason;
                Expected = expected;
                Actual = actual;
            }

            public long MovieTick { get; }
            public string Reason { get; }
            public RngStateRecord? Expected { get; }
            public RngStateRecord? Actual { get; }
        }

        private sealed class CallDifference
        {
            public CallDifference(
                long movieTick,
                string reason,
                RngCallRecord? expected,
                RngCallRecord? actual)
            {
                MovieTick = movieTick;
                Reason = reason;
                Expected = expected;
                Actual = actual;
            }

            public long MovieTick { get; }
            public string Reason { get; }
            public RngCallRecord? Expected { get; }
            public RngCallRecord? Actual { get; }
        }
    }
}
