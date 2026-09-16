using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HollowKnightTAS.Core.Input;

namespace HollowKnightTAS.Core.Movie
{
    public enum TimelineEditKind : byte
    {
        Replace = 1,
        Insert = 2,
        Delete = 3
    }

    public sealed class MovieTimelineEditResult
    {
        internal MovieTimelineEditResult(
            MovieDocument movie,
            TimelineEditKind kind,
            long startTick,
            long deletedTicks,
            long insertedTicks,
            long previousExpandedTicks,
            IEnumerable<string> invalidatedCheckpoints)
        {
            Movie = movie ?? throw new ArgumentNullException(nameof(movie));
            Kind = kind;
            StartTick = startTick;
            DeletedTicks = deletedTicks;
            InsertedTicks = insertedTicks;
            PreviousExpandedTicks = previousExpandedTicks;
            ExpandedTicks = checked(
                previousExpandedTicks - deletedTicks + insertedTicks);
            InvalidatedCheckpoints =
                new ReadOnlyCollection<string>(
                    new List<string>(invalidatedCheckpoints));
        }

        public MovieDocument Movie { get; }
        public TimelineEditKind Kind { get; }
        public long StartTick { get; }
        public long DeletedTicks { get; }
        public long InsertedTicks { get; }
        public long PreviousExpandedTicks { get; }
        public long ExpandedTicks { get; }
        public long TickDelta => InsertedTicks - DeletedTicks;
        public IReadOnlyList<string> InvalidatedCheckpoints { get; }
    }

    /// <summary>
    /// Applies typed edits in input-tick coordinates without expanding a
    /// movie into one managed object per tick. Non-input commands retain
    /// their tick anchor; commands inside a deleted range are invalidated.
    /// </summary>
    public static class MovieTimelineEditor
    {
        public static MovieTimelineEditResult Replace(
            MovieDocument movie,
            long startTick,
            long deleteCount,
            IEnumerable<FrameRunCommand> replacement)
        {
            return Edit(
                movie,
                TimelineEditKind.Replace,
                startTick,
                deleteCount,
                replacement);
        }

        public static MovieTimelineEditResult Insert(
            MovieDocument movie,
            long startTick,
            IEnumerable<FrameRunCommand> frames)
        {
            return Edit(
                movie,
                TimelineEditKind.Insert,
                startTick,
                0,
                frames);
        }

        public static MovieTimelineEditResult Delete(
            MovieDocument movie,
            long startTick,
            long count)
        {
            return Edit(
                movie,
                TimelineEditKind.Delete,
                startTick,
                count,
                Array.Empty<FrameRunCommand>());
        }

        public static MovieTimelineEditResult Edit(
            MovieDocument movie,
            TimelineEditKind kind,
            long startTick,
            long deleteCount,
            IEnumerable<FrameRunCommand> replacement)
        {
            if (movie == null)
            {
                throw new ArgumentNullException(nameof(movie));
            }

            if (!Enum.IsDefined(typeof(TimelineEditKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            if (startTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(startTick));
            }

            if (deleteCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(deleteCount));
            }

            if (replacement == null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            if (kind == TimelineEditKind.Insert && deleteCount != 0)
            {
                throw new ArgumentException(
                    "Insert cannot delete input ticks.",
                    nameof(deleteCount));
            }

            var replacementArray = replacement.ToArray();
            if (kind == TimelineEditKind.Delete
                && replacementArray.Length != 0)
            {
                throw new ArgumentException(
                    "Delete cannot insert input ticks.",
                    nameof(replacement));
            }

            var baseTimeline = ReadTimeline(movie);
            var replacementRuns = NormalizeRuns(replacementArray);
            var insertedTicks = CountTicks(replacementRuns);
            if (kind == TimelineEditKind.Insert && insertedTicks == 0)
            {
                throw new ArgumentException(
                    "Insert requires at least one input tick.",
                    nameof(replacement));
            }

            if (kind == TimelineEditKind.Delete && deleteCount == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deleteCount),
                    "Delete requires at least one input tick.");
            }

            if (kind == TimelineEditKind.Replace
                && deleteCount == 0
                && insertedTicks == 0)
            {
                throw new ArgumentException(
                    "Replace must delete or insert at least one input tick.");
            }

            if (startTick > baseTimeline.ExpandedTicks
                || deleteCount
                   > baseTimeline.ExpandedTicks - startTick)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(startTick),
                    "The edited range must be inside the movie timeline.");
            }

            var endTick = checked(startTick + deleteCount);
            var editedRuns = new List<InputRun>();
            AppendSlice(
                editedRuns,
                baseTimeline.Runs,
                0,
                startTick);
            foreach (var run in replacementRuns)
            {
                AppendRun(editedRuns, run);
            }

            AppendSlice(
                editedRuns,
                baseTimeline.Runs,
                endTick,
                baseTimeline.ExpandedTicks);

            var invalidatedCheckpoints = new List<string>();
            var mappedEvents = new List<TimelineEvent>();
            foreach (var value in baseTimeline.Events)
            {
                if (deleteCount > 0
                    && value.Tick >= startTick
                    && value.Tick < endTick)
                {
                    if (value.Command is CheckpointCommand checkpoint)
                    {
                        invalidatedCheckpoints.Add(
                            checkpoint.Identifier);
                    }

                    continue;
                }

                var mappedTick = value.Tick;
                if (deleteCount == 0)
                {
                    if (mappedTick >= startTick)
                    {
                        mappedTick = checked(
                            mappedTick + insertedTicks);
                    }
                }
                else if (mappedTick >= endTick)
                {
                    mappedTick = checked(
                        mappedTick - deleteCount + insertedTicks);
                }

                mappedEvents.Add(
                    new TimelineEvent(
                        mappedTick,
                        value.Order,
                        value.Command));
            }

            mappedEvents.Sort(TimelineEvent.Compare);
            var commands = ComposeCommands(
                editedRuns,
                mappedEvents,
                movie.SourceName);
            var edited = new MovieDocument(
                movie.SourceName,
                movie.Header,
                commands);
            return new MovieTimelineEditResult(
                edited,
                kind,
                startTick,
                deleteCount,
                insertedTicks,
                baseTimeline.ExpandedTicks,
                invalidatedCheckpoints);
        }

        private static Timeline ReadTimeline(MovieDocument movie)
        {
            var runs = new List<InputRun>();
            var events = new List<TimelineEvent>();
            var tick = 0L;
            var order = 0;
            foreach (var command in movie.Commands)
            {
                if (command is FrameRunCommand frames)
                {
                    if (frames.FrameCount <= 0)
                    {
                        throw new ArgumentException(
                            "Movie frame runs must be positive.",
                            nameof(movie));
                    }

                    AppendRun(runs, InputRun.From(frames));
                    tick = checked(tick + frames.FrameCount);
                }
                else
                {
                    events.Add(
                        new TimelineEvent(
                            tick,
                            order++,
                            command));
                }
            }

            return new Timeline(runs, events, tick);
        }

        private static List<InputRun> NormalizeRuns(
            IEnumerable<FrameRunCommand> frames)
        {
            var runs = new List<InputRun>();
            foreach (var frame in frames)
            {
                if (frame == null || frame.FrameCount <= 0)
                {
                    throw new ArgumentException(
                        "Replacement frame runs must be non-null and positive.",
                        nameof(frames));
                }

                AppendRun(runs, InputRun.From(frame));
            }

            return runs;
        }

        private static long CountTicks(IEnumerable<InputRun> runs)
        {
            var result = 0L;
            foreach (var run in runs)
            {
                result = checked(result + run.Count);
            }

            return result;
        }

        private static void AppendSlice(
            ICollection<InputRun> destination,
            IReadOnlyList<InputRun> source,
            long startTick,
            long endTick)
        {
            if (startTick == endTick)
            {
                return;
            }

            var tick = 0L;
            foreach (var run in source)
            {
                var runEnd = checked(tick + run.Count);
                var overlapStart = Math.Max(tick, startTick);
                var overlapEnd = Math.Min(runEnd, endTick);
                if (overlapEnd > overlapStart)
                {
                    AppendRun(
                        destination,
                        run.WithCount(overlapEnd - overlapStart));
                }

                tick = runEnd;
                if (tick >= endTick)
                {
                    return;
                }
            }
        }

        private static void AppendRun(
            ICollection<InputRun> destination,
            InputRun run)
        {
            if (destination is List<InputRun> list
                && list.Count > 0
                && list[list.Count - 1].HasSameInput(run))
            {
                var previous = list[list.Count - 1];
                list[list.Count - 1] = previous.WithCount(
                    checked(previous.Count + run.Count));
                return;
            }

            destination.Add(run);
        }

        private static IReadOnlyList<MovieCommand> ComposeCommands(
            IReadOnlyList<InputRun> runs,
            IReadOnlyList<TimelineEvent> events,
            string sourceName)
        {
            var result = new List<MovieCommand>();
            var span = new MovieSourceSpan(
                string.IsNullOrWhiteSpace(sourceName)
                    ? "<timeline-edit>"
                    : sourceName,
                1,
                1,
                1);
            var eventIndex = 0;
            var tick = 0L;
            foreach (var run in runs)
            {
                var remaining = run.Count;
                while (remaining > 0)
                {
                    while (eventIndex < events.Count
                           && events[eventIndex].Tick == tick)
                    {
                        result.Add(events[eventIndex++].Command);
                    }

                    var nextEventTick = eventIndex < events.Count
                        ? events[eventIndex].Tick
                        : long.MaxValue;
                    var length = Math.Min(
                        remaining,
                        nextEventTick - tick);
                    if (length <= 0)
                    {
                        throw new InvalidOperationException(
                            "Timeline event order is inconsistent.");
                    }

                    result.Add(run.ToCommand(length, span));
                    tick = checked(tick + length);
                    remaining -= length;
                }
            }

            while (eventIndex < events.Count
                   && events[eventIndex].Tick == tick)
            {
                result.Add(events[eventIndex++].Command);
            }

            if (eventIndex != events.Count)
            {
                throw new InvalidOperationException(
                    "A timeline event falls outside the edited movie.");
            }

            return new ReadOnlyCollection<MovieCommand>(result);
        }

        private sealed class Timeline
        {
            public Timeline(
                IReadOnlyList<InputRun> runs,
                IReadOnlyList<TimelineEvent> events,
                long expandedTicks)
            {
                Runs = runs;
                Events = events;
                ExpandedTicks = expandedTicks;
            }

            public IReadOnlyList<InputRun> Runs { get; }
            public IReadOnlyList<TimelineEvent> Events { get; }
            public long ExpandedTicks { get; }
        }

        private readonly struct InputRun
        {
            public InputRun(
                long count,
                TasAction held,
                int axisX,
                int axisY,
                bool hasAnalogAxes)
            {
                Count = count;
                Held = held;
                AxisX = axisX;
                AxisY = axisY;
                HasAnalogAxes = hasAnalogAxes;
            }

            public long Count { get; }
            public TasAction Held { get; }
            public int AxisX { get; }
            public int AxisY { get; }
            public bool HasAnalogAxes { get; }

            public static InputRun From(FrameRunCommand value)
            {
                return new InputRun(
                    value.FrameCount,
                    value.HeldActions,
                    value.AxisX,
                    value.AxisY,
                    value.HasAnalogAxes);
            }

            public bool HasSameInput(InputRun other)
            {
                return Held == other.Held
                       && AxisX == other.AxisX
                       && AxisY == other.AxisY;
            }

            public InputRun WithCount(long count)
            {
                return new InputRun(
                    count,
                    Held,
                    AxisX,
                    AxisY,
                    HasAnalogAxes);
            }

            public FrameRunCommand ToCommand(
                long count,
                MovieSourceSpan span)
            {
                return new FrameRunCommand(
                    count,
                    Held,
                    AxisX,
                    AxisY,
                    HasAnalogAxes,
                    span);
            }
        }

        private sealed class TimelineEvent
        {
            public TimelineEvent(
                long tick,
                int order,
                MovieCommand command)
            {
                Tick = tick;
                Order = order;
                Command = command;
            }

            public long Tick { get; }
            public int Order { get; }
            public MovieCommand Command { get; }

            public static int Compare(
                TimelineEvent left,
                TimelineEvent right)
            {
                var tick = left.Tick.CompareTo(right.Tick);
                return tick != 0 ? tick : left.Order.CompareTo(right.Order);
            }
        }
    }
}
