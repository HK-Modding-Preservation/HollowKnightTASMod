using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HollowKnightTAS.Core.Ledger;

namespace HollowKnightTAS.Core.Rng
{
    public sealed class RngCallRecord
    {
        public RngCallRecord(
            long movieTick,
            TickStamp tickStamp,
            string callSiteId,
            long globalCallIndex,
            long callSiteIndex,
            string beforeStateSha256,
            string afterStateSha256)
        {
            if (movieTick < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(movieTick));
            }

            if (globalCallIndex <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(globalCallIndex));
            }

            if (callSiteIndex <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(callSiteIndex));
            }

            MovieTick = movieTick;
            TickStamp = tickStamp;
            CallSiteId = RngValidation.RequireIdentifier(
                callSiteId,
                nameof(callSiteId));
            GlobalCallIndex = globalCallIndex;
            CallSiteIndex = callSiteIndex;
            BeforeStateSha256 = RngValidation.RequireSha256(
                beforeStateSha256,
                nameof(beforeStateSha256));
            AfterStateSha256 = RngValidation.RequireSha256(
                afterStateSha256,
                nameof(afterStateSha256));
        }

        public long MovieTick { get; }
        public TickStamp TickStamp { get; }
        public string CallSiteId { get; }
        public long GlobalCallIndex { get; }
        public long CallSiteIndex { get; }
        public string BeforeStateSha256 { get; }
        public string AfterStateSha256 { get; }
        public bool StateChanged => !string.Equals(
            BeforeStateSha256,
            AfterStateSha256,
            StringComparison.Ordinal);
    }

    public sealed class RngStateRecord
    {
        private readonly ReadOnlyDictionary<string, long> callSiteCounts;

        public RngStateRecord(
            long movieTick,
            TickStamp tickStamp,
            string stateSha256,
            long globalCallCount,
            IReadOnlyDictionary<string, long> callSiteCounts)
        {
            if (movieTick < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(movieTick));
            }

            if (globalCallCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(globalCallCount));
            }

            if (callSiteCounts == null)
            {
                throw new ArgumentNullException(nameof(callSiteCounts));
            }

            var copied = new SortedDictionary<string, long>(
                StringComparer.Ordinal);
            foreach (var item in callSiteCounts)
            {
                var id = RngValidation.RequireIdentifier(
                    item.Key,
                    nameof(callSiteCounts));
                if (item.Value < 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(callSiteCounts));
                }

                copied.Add(id, item.Value);
            }

            MovieTick = movieTick;
            TickStamp = tickStamp;
            StateSha256 = RngValidation.RequireSha256(
                stateSha256,
                nameof(stateSha256));
            GlobalCallCount = globalCallCount;
            this.callSiteCounts =
                new ReadOnlyDictionary<string, long>(copied);
        }

        public long MovieTick { get; }
        public TickStamp TickStamp { get; }
        public string StateSha256 { get; }
        public long GlobalCallCount { get; }
        public IReadOnlyDictionary<string, long> CallSiteCounts =>
            callSiteCounts;
    }
}
