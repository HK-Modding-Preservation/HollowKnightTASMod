using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HollowKnightTAS.Core.Verification
{
    public sealed class RunEvidence
    {
        public RunEvidence(
            string sessionId,
            string processInstanceId,
            string manifestSha256,
            string baselineSha256,
            string movieId,
            int snapshotSchemaVersion,
            int ledgerSchemaVersion,
            IEnumerable<MilestoneRecord> milestones)
        {
            SessionId = RequireText(sessionId, nameof(sessionId));
            ProcessInstanceId = RequireText(
                processInstanceId,
                nameof(processInstanceId));
            ManifestSha256 =
                HollowKnightTAS.Core.Verification.RunSignature.RequireSha256(
                manifestSha256,
                nameof(manifestSha256));
            BaselineSha256 =
                HollowKnightTAS.Core.Verification.RunSignature.RequireSha256(
                baselineSha256,
                nameof(baselineSha256));
            MovieId =
                HollowKnightTAS.Core.Verification.RunSignature.RequireSha256(
                    movieId,
                    nameof(movieId));
            if (snapshotSchemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(snapshotSchemaVersion));
            }

            if (ledgerSchemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ledgerSchemaVersion));
            }

            SnapshotSchemaVersion = snapshotSchemaVersion;
            LedgerSchemaVersion = ledgerSchemaVersion;
            var records = milestones == null
                ? throw new ArgumentNullException(nameof(milestones))
                : new List<MilestoneRecord>(milestones);
            if (records.Count == 0)
            {
                throw new ArgumentException(
                    "At least one milestone is required.",
                    nameof(milestones));
            }

            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            long previousTick = -1;
            foreach (var record in records)
            {
                if (record == null)
                {
                    throw new ArgumentException(
                        "Milestones cannot contain null.",
                        nameof(milestones));
                }

                if (!identifiers.Add(record.MilestoneId))
                {
                    throw new ArgumentException(
                        "Milestone IDs must be unique.",
                        nameof(milestones));
                }

                if (record.MovieTick < previousTick)
                {
                    throw new ArgumentException(
                        "Milestones must be ordered by movie tick.",
                        nameof(milestones));
                }

                previousTick = record.MovieTick;
            }

            Milestones = new ReadOnlyCollection<MilestoneRecord>(records);
            RunSignature =
                HollowKnightTAS.Core.Verification.RunSignature.Compute(this);
        }

        public string SessionId { get; }
        public string ProcessInstanceId { get; }
        public string ManifestSha256 { get; }
        public string BaselineSha256 { get; }
        public string MovieId { get; }
        public int SnapshotSchemaVersion { get; }
        public int LedgerSchemaVersion { get; }
        public IReadOnlyList<MilestoneRecord> Milestones { get; }
        public string RunSignature { get; }

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
