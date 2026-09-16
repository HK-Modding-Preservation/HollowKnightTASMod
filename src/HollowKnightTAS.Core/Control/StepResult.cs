using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HollowKnightTAS.Core.Ledger;

namespace HollowKnightTAS.Core.Control
{
    public sealed class StepResult
    {
        private readonly ReadOnlyCollection<TickLedgerRecord> ledger;

        public StepResult(
            StepRequest request,
            int movieTickDelta,
            long visualTickDelta,
            long fixedTickDelta,
            ulong inputTickDelta,
            IReadOnlyList<TickLedgerRecord> ledger)
        {
            if (movieTickDelta < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(movieTickDelta));
            }

            if (visualTickDelta < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(visualTickDelta));
            }

            if (fixedTickDelta < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedTickDelta));
            }

            if (ledger == null)
            {
                throw new ArgumentNullException(nameof(ledger));
            }

            var copy = new List<TickLedgerRecord>(ledger.Count);
            foreach (var record in ledger)
            {
                copy.Add(
                    record
                    ?? throw new ArgumentException(
                        "Step ledger cannot contain null records.",
                        nameof(ledger)));
            }

            Request = request;
            MovieTickDelta = movieTickDelta;
            VisualTickDelta = visualTickDelta;
            FixedTickDelta = fixedTickDelta;
            InputTickDelta = inputTickDelta;
            this.ledger = new ReadOnlyCollection<TickLedgerRecord>(copy);
        }

        public StepRequest Request { get; }
        public int MovieTickDelta { get; }
        public long VisualTickDelta { get; }
        public long FixedTickDelta { get; }
        public ulong InputTickDelta { get; }
        public IReadOnlyList<TickLedgerRecord> Ledger => ledger;
    }
}
