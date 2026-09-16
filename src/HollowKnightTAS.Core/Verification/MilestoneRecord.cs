using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.State;

namespace HollowKnightTAS.Core.Verification
{
    public sealed class MilestoneRecord
    {
        private readonly byte[] semanticSnapshotBytes;

        public MilestoneRecord(
            string milestoneId,
            long movieTick,
            TickStamp tickStamp,
            string sceneName,
            byte[] semanticSnapshotBytes,
            InputSample input,
            IEnumerable<VerificationLedgerEntry> ledgerWindow,
            string rngStateSha256 = VerificationLedgerEntry.RngNotCaptured)
        {
            MilestoneId = RequireText(milestoneId, nameof(milestoneId));
            if (movieTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(movieTick));
            }

            MovieTick = movieTick;
            TickStamp = tickStamp;
            SceneName = sceneName
                        ?? throw new ArgumentNullException(nameof(sceneName));
            if (semanticSnapshotBytes == null)
            {
                throw new ArgumentNullException(nameof(semanticSnapshotBytes));
            }

            var snapshotBytes = (byte[])semanticSnapshotBytes.Clone();
            var snapshot = SemanticSnapshotCanonicalizer.Deserialize(snapshotBytes);
            this.semanticSnapshotBytes = snapshotBytes;
            SemanticSha256 = SemanticSnapshotHasher.ComputeSha256(snapshot);
            Input = input;
            var entries = ledgerWindow == null
                ? throw new ArgumentNullException(nameof(ledgerWindow))
                : new List<VerificationLedgerEntry>(ledgerWindow);
            if (entries.Exists(value => value == null))
            {
                throw new ArgumentException(
                    "Ledger window cannot contain null entries.",
                    nameof(ledgerWindow));
            }

            LedgerWindow = new ReadOnlyCollection<VerificationLedgerEntry>(entries);
            LedgerWindowSha256 = RunSignature.ComputeLedgerWindowSha256(entries);
            RngStateSha256 = RunSignature.RequireOptionalSha256(
                rngStateSha256,
                nameof(rngStateSha256));
        }

        public string MilestoneId { get; }
        public long MovieTick { get; }
        public TickStamp TickStamp { get; }
        public string SceneName { get; }
        public string SemanticSha256 { get; }
        public string LedgerWindowSha256 { get; }
        public byte[] SemanticSnapshotBytes =>
            (byte[])semanticSnapshotBytes.Clone();
        public InputSample Input { get; }
        public IReadOnlyList<VerificationLedgerEntry> LedgerWindow { get; }
        public string RngStateSha256 { get; }

        public SemanticSnapshot ReadSemanticSnapshot()
        {
            return SemanticSnapshotCanonicalizer.Deserialize(
                (byte[])semanticSnapshotBytes.Clone());
        }

        private static string RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty value is required.",
                    name);
            }

            return value;
        }
    }
}
