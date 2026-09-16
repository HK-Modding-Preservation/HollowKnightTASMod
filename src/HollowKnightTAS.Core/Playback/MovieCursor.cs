using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Core.Playback
{
    public sealed class MovieCursor
    {
        private readonly MovieDocument movie;
        private readonly List<PlaybackEvent> pendingEvents =
            new List<PlaybackEvent>();
        private FrameRunCommand? currentRun;
        private int nextCommandIndex;
        private TasAction previousHeld;

        public MovieCursor(MovieDocument movie) : this(movie, TasAction.None)
        {
        }

        public MovieCursor(MovieDocument movie, TasAction initialHeld)
        {
            this.movie = movie ?? throw new ArgumentNullException(nameof(movie));
            previousHeld = initialHeld;
            MovieTick = -1;
            CommandIndex = -1;
            OffsetWithinFrameRun = -1;
            CurrentInput = InputSample.FromHeld(
                0,
                TasAction.None,
                TasAction.None);
        }

        public long MovieTick { get; private set; }
        public int CommandIndex { get; private set; }
        public long OffsetWithinFrameRun { get; private set; }
        public InputSample CurrentInput { get; private set; }
        public bool IsComplete { get; private set; }
        public IReadOnlyList<PlaybackEvent> PendingEvents =>
            new ReadOnlyCollection<PlaybackEvent>(pendingEvents);

        public bool MoveNext()
        {
            if (IsComplete)
            {
                pendingEvents.Clear();
                return false;
            }

            pendingEvents.Clear();
            if (currentRun != null
                && OffsetWithinFrameRun + 1 < currentRun.FrameCount)
            {
                OffsetWithinFrameRun++;
                MoveToInput(currentRun);
                return true;
            }

            currentRun = null;
            OffsetWithinFrameRun = -1;
            while (nextCommandIndex < movie.Commands.Count)
            {
                var index = nextCommandIndex++;
                var command = movie.Commands[index];
                if (command is FrameRunCommand frames)
                {
                    if (frames.FrameCount <= 0)
                    {
                        throw new InvalidOperationException(
                            "Movie cursor received a non-positive frame run.");
                    }

                    currentRun = frames;
                    CommandIndex = index;
                    OffsetWithinFrameRun = 0;
                    MoveToInput(frames);
                    return true;
                }

                pendingEvents.Add(
                    PlaybackEvent.FromCommand(
                        command,
                        checked(MovieTick + 1),
                        index));
            }

            IsComplete = true;
            CommandIndex = movie.Commands.Count;
            OffsetWithinFrameRun = -1;
            var releaseTick = checked(MovieTick + 1);
            CurrentInput = InputSample.FromHeld(
                checked((ulong)releaseTick),
                TasAction.None,
                previousHeld);
            previousHeld = TasAction.None;
            return false;
        }

        private void MoveToInput(FrameRunCommand run)
        {
            MovieTick = checked(MovieTick + 1);
            var axisX = checked((short)run.AxisX);
            var axisY = checked((short)run.AxisY);
            CurrentInput = InputSample.FromHeld(
                checked((ulong)MovieTick),
                run.HeldActions,
                previousHeld,
                axisX,
                axisY);
            previousHeld = run.HeldActions;
        }
    }
}
