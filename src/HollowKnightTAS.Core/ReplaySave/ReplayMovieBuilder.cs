using System;
using System.Collections.Generic;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.Recording;

namespace HollowKnightTAS.Core.ReplaySave
{
    public sealed class ReplayMovieEvent
    {
        public ReplayMovieEvent(long beforeMovieTick, MovieCommand command)
        {
            if (beforeMovieTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(beforeMovieTick));
            }

            BeforeMovieTick = beforeMovieTick;
            Command = command ?? throw new ArgumentNullException(nameof(command));
        }

        public long BeforeMovieTick { get; }
        public MovieCommand Command { get; }
    }

    public static class ReplayMovieBuilder
    {
        public static MovieDocument Build(
            IReadOnlyList<JournalRecord> records,
            string sourceName,
            string gameVersion,
            string apiVersion,
            string manifestSha256,
            string baselineId,
            string baselineSemanticSha256)
        {
            return Build(
                records,
                Array.Empty<ReplayMovieEvent>(),
                sourceName,
                gameVersion,
                apiVersion,
                manifestSha256,
                baselineId,
                baselineSemanticSha256);
        }

        public static MovieDocument Build(
            IReadOnlyList<JournalRecord> records,
            IEnumerable<ReplayMovieEvent> events,
            string sourceName,
            string gameVersion,
            string apiVersion,
            string manifestSha256,
            string baselineId,
            string baselineSemanticSha256)
        {
            if (records == null)
            {
                throw new ArgumentNullException(nameof(records));
            }

            if (records.Count == 0)
            {
                throw new ArgumentException(
                    "A replay movie requires at least one journal record.",
                    nameof(records));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            var commands = new List<MovieCommand>();
            var span = new MovieSourceSpan(
                string.IsNullOrWhiteSpace(sourceName) ? "replay-save.hktas" : sourceName,
                1,
                1,
                1);
            var expectedMovieTick = 0L;
            var runHeld = TasAction.None;
            short runAxisX = 0;
            short runAxisY = 0;
            long runLength = 0;
            var orderedEvents = new List<ReplayMovieEvent>(events);
            orderedEvents.Sort(
                (left, right) =>
                {
                    var tick = left.BeforeMovieTick.CompareTo(
                        right.BeforeMovieTick);
                    return tick != 0
                        ? tick
                        : left.Command.Span.Line.CompareTo(
                            right.Command.Span.Line);
                });
            var eventIndex = 0;
            foreach (var record in records)
            {
                if (record.MovieTick != expectedMovieTick)
                {
                    throw new ArgumentException(
                        "Replay journal prefix must begin at movie tick zero and be contiguous.",
                        nameof(records));
                }

                while (eventIndex < orderedEvents.Count
                       && orderedEvents[eventIndex].BeforeMovieTick
                          == record.MovieTick)
                {
                    FlushRun(
                        commands,
                        span,
                        ref runLength,
                        runHeld,
                        runAxisX,
                        runAxisY);
                    commands.Add(orderedEvents[eventIndex].Command);
                    eventIndex++;
                }

                if (eventIndex < orderedEvents.Count
                    && orderedEvents[eventIndex].BeforeMovieTick
                       < record.MovieTick)
                {
                    throw new ArgumentException(
                        "Replay movie event ticks must be ordered within the journal prefix.",
                        nameof(events));
                }

                expectedMovieTick = checked(expectedMovieTick + 1);
                var sample = record.Sample;
                if (runLength > 0
                    && (sample.Held != runHeld
                        || sample.AxisX != runAxisX
                        || sample.AxisY != runAxisY))
                {
                    FlushRun(
                        commands,
                        span,
                        ref runLength,
                        runHeld,
                        runAxisX,
                        runAxisY);
                }

                if (runLength == 0)
                {
                    runHeld = sample.Held;
                    runAxisX = sample.AxisX;
                    runAxisY = sample.AxisY;
                }

                runLength = checked(runLength + 1);
            }

            FlushRun(
                commands,
                span,
                ref runLength,
                runHeld,
                runAxisX,
                runAxisY);
            while (eventIndex < orderedEvents.Count
                   && orderedEvents[eventIndex].BeforeMovieTick
                      == records.Count)
            {
                commands.Add(orderedEvents[eventIndex++].Command);
            }

            if (eventIndex != orderedEvents.Count)
            {
                throw new ArgumentException(
                    "Replay movie event falls outside the journal prefix.",
                    nameof(events));
            }

            return new MovieDocument(
                sourceName,
                new MovieHeader(
                    MovieProtocolV1.Version,
                    gameVersion,
                    apiVersion,
                    manifestSha256,
                    baselineId,
                    baselineSemanticSha256,
                    "input"),
                commands);
        }

        private static void FlushRun(
            ICollection<MovieCommand> commands,
            MovieSourceSpan span,
            ref long runLength,
            TasAction runHeld,
            short runAxisX,
            short runAxisY)
        {
            if (runLength == 0)
            {
                return;
            }

            commands.Add(
                new FrameRunCommand(
                    runLength,
                    runHeld,
                    runAxisX,
                    runAxisY,
                    runAxisX != 0 || runAxisY != 0,
                    span));
            runLength = 0;
        }
    }
}
