using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Ledger;

namespace HollowKnightTAS.Core.Recording
{
    public sealed class JournalRecord
    {
        internal JournalRecord(
            long movieTick,
            InputSample sample,
            TickStamp stamp)
        {
            MovieTick = movieTick;
            Sample = sample;
            Stamp = stamp;
        }

        public long MovieTick { get; }
        public InputSample Sample { get; }
        public TickStamp Stamp { get; }

        public static JournalRecord Create(
            long movieTick,
            InputSample sample,
            TickStamp stamp)
        {
            if (movieTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(movieTick));
            }

            if (sample.InputTick != stamp.InputTick
                || stamp.Phase != TickPhase.InControlCommitted)
            {
                throw new ArgumentException(
                    "Journal records require a matching committed input tick.");
            }

            return new JournalRecord(movieTick, sample, stamp);
        }
    }

    public sealed class JournalAppendResult
    {
        internal JournalAppendResult(
            bool success,
            long movieTick,
            string error)
        {
            Success = success;
            MovieTick = movieTick;
            Error = error;
        }

        public bool Success { get; }
        public long MovieTick { get; }
        public string Error { get; }
    }

    public sealed class JournalSlice
    {
        internal JournalSlice(
            long firstMovieTick,
            long lastMovieTick,
            IReadOnlyList<JournalRecord> records)
        {
            FirstMovieTick = firstMovieTick;
            LastMovieTick = lastMovieTick;
            Records = records;
        }

        public long FirstMovieTick { get; }
        public long LastMovieTick { get; }
        public IReadOnlyList<JournalRecord> Records { get; }
    }

    public sealed class ReplayJournal
    {
        private readonly List<JournalRecord> retained =
            new List<JournalRecord>();
        private ulong previousRawInputTick;
        private bool hasPreviousRawInputTick;

        public ReplayJournal(string baselineId, string baselineSha256)
        {
            if (string.IsNullOrWhiteSpace(baselineId))
            {
                throw new ArgumentException(
                    "A baseline ID is required.",
                    nameof(baselineId));
            }

            if (string.IsNullOrWhiteSpace(baselineSha256))
            {
                throw new ArgumentException(
                    "A baseline SHA-256 is required.",
                    nameof(baselineSha256));
            }

            BaselineId = baselineId;
            BaselineSha256 = baselineSha256;
        }

        public string BaselineId { get; }
        public string BaselineSha256 { get; }
        public long FirstMovieTick => LastCommittedMovieTick < 0 ? -1 : 0;
        public long LastCommittedMovieTick { get; private set; } = -1;
        public InputSample? LastCommittedSample { get; private set; }
        public long LastPersistedMovieTick { get; private set; } = -1;
        public bool HasGap { get; private set; }
        public int RetainedCount => retained.Count;

        public JournalAppendResult Append(InputSample sample, TickStamp stamp)
        {
            if (HasGap)
            {
                return Failure("Journal is already invalid because it has a gap.");
            }

            if (stamp.Phase != TickPhase.InControlCommitted)
            {
                return MarkGap("Journal append phase must be InControlCommitted.");
            }

            if (sample.InputTick != stamp.InputTick)
            {
                return MarkGap("InputSample tick does not match TickStamp input tick.");
            }

            if (hasPreviousRawInputTick
                && sample.InputTick != previousRawInputTick + 1)
            {
                return MarkGap("Raw InControl tick gap detected.");
            }

            var movieTick = checked(LastCommittedMovieTick + 1);
            retained.Add(new JournalRecord(movieTick, sample, stamp));
            LastCommittedMovieTick = movieTick;
            LastCommittedSample = sample;
            previousRawInputTick = sample.InputTick;
            hasPreviousRawInputTick = true;
            return new JournalAppendResult(true, movieTick, string.Empty);
        }

        public void Suspend()
        {
            hasPreviousRawInputTick = false;
        }

        public JournalSlice CommitThrough(long movieTick)
        {
            if (movieTick < LastPersistedMovieTick
                || movieTick > LastCommittedMovieTick)
            {
                throw new ArgumentOutOfRangeException(nameof(movieTick));
            }

            var records = retained
                .Where(
                    value => value.MovieTick > LastPersistedMovieTick
                             && value.MovieTick <= movieTick)
                .ToArray();
            var first = records.Length == 0
                ? checked(LastPersistedMovieTick + 1)
                : records[0].MovieTick;
            return new JournalSlice(
                first,
                movieTick,
                new ReadOnlyCollection<JournalRecord>(records));
        }

        public void AcknowledgePersistedThrough(long movieTick)
        {
            if (movieTick < LastPersistedMovieTick
                || movieTick > LastCommittedMovieTick)
            {
                throw new ArgumentOutOfRangeException(nameof(movieTick));
            }

            retained.RemoveAll(value => value.MovieTick <= movieTick);
            LastPersistedMovieTick = movieTick;
        }

        public void MarkPersistenceFailure(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                throw new ArgumentException(
                    "A persistence error is required.",
                    nameof(error));
            }

            HasGap = true;
        }

        private JournalAppendResult MarkGap(string error)
        {
            HasGap = true;
            return Failure(error);
        }

        private JournalAppendResult Failure(string error)
        {
            return new JournalAppendResult(false, LastCommittedMovieTick, error);
        }
    }
}
