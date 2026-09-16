using System;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Core.Playback
{
    public enum PlaybackEventKind : byte
    {
        Marker = 1,
        Checkpoint = 2,
        Assertion = 3
    }

    public sealed class PlaybackEvent
    {
        private PlaybackEvent(
            PlaybackEventKind kind,
            long movieTick,
            int commandIndex,
            MovieCommand command)
        {
            Kind = kind;
            MovieTick = movieTick;
            CommandIndex = commandIndex;
            Command = command ?? throw new ArgumentNullException(nameof(command));
        }

        public PlaybackEventKind Kind { get; }
        public long MovieTick { get; }
        public int CommandIndex { get; }
        public MovieCommand Command { get; }

        internal static PlaybackEvent FromCommand(
            MovieCommand command,
            long movieTick,
            int commandIndex)
        {
            if (command is MarkerCommand)
            {
                return new PlaybackEvent(
                    PlaybackEventKind.Marker,
                    movieTick,
                    commandIndex,
                    command);
            }

            if (command is CheckpointCommand)
            {
                return new PlaybackEvent(
                    PlaybackEventKind.Checkpoint,
                    movieTick,
                    commandIndex,
                    command);
            }

            if (command is AssertCommand)
            {
                return new PlaybackEvent(
                    PlaybackEventKind.Assertion,
                    movieTick,
                    commandIndex,
                    command);
            }

            throw new ArgumentException(
                "The command is not a playback event.",
                nameof(command));
        }
    }
}
