using System;
using System.Collections.Generic;
using HollowKnightTAS.Core.State;

namespace HollowKnightTAS.Core.Verification
{
    public sealed class DesyncReport
    {
        internal DesyncReport(
            int firstMilestoneIndex,
            MilestoneRecord expected,
            MilestoneRecord actual,
            SemanticDiff semanticDiff,
            string reason)
        {
            if (firstMilestoneIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(firstMilestoneIndex));
            }

            FirstMilestoneIndex = firstMilestoneIndex;
            Expected = expected
                       ?? throw new ArgumentNullException(nameof(expected));
            Actual = actual
                     ?? throw new ArgumentNullException(nameof(actual));
            SemanticDiff = semanticDiff
                           ?? throw new ArgumentNullException(
                               nameof(semanticDiff));
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }

        public int FirstMilestoneIndex { get; }
        public string FirstMilestoneId => Expected.MilestoneId;
        public long FirstMovieTick => Expected.MovieTick;
        public MilestoneRecord Expected { get; }
        public MilestoneRecord Actual { get; }
        public SemanticDiff SemanticDiff { get; }
        public string Reason { get; }
        public IReadOnlyList<VerificationLedgerEntry> ExpectedLedgerWindow =>
            Expected.LedgerWindow;
        public IReadOnlyList<VerificationLedgerEntry> ActualLedgerWindow =>
            Actual.LedgerWindow;
    }
}
