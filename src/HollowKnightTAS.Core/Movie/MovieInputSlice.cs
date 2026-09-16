using System;
using System.Collections.Generic;
using System.Linq;

namespace HollowKnightTAS.Core.Movie
{
    public static class MovieInputSlice
    {
        public static MovieDocument Extract(
            MovieDocument movie,
            long startTick,
            long count,
            string sourceName = "input-slice.hktas")
        {
            if (movie == null)
            {
                throw new ArgumentNullException(nameof(movie));
            }

            var total = movie.Commands
                .OfType<FrameRunCommand>()
                .Aggregate(
                    0L,
                    (value, command) => checked(
                        value + command.FrameCount));
            if (startTick < 0
                || count < 0
                || startTick > total
                || count > total - startTick)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(startTick),
                    "Input slice must be contained in the movie timeline.");
            }

            var endTick = checked(startTick + count);
            var cursor = 0L;
            var commands = new List<MovieCommand>();
            foreach (var frame in movie.Commands.OfType<FrameRunCommand>())
            {
                var frameEnd = checked(cursor + frame.FrameCount);
                var overlapStart = Math.Max(cursor, startTick);
                var overlapEnd = Math.Min(frameEnd, endTick);
                if (overlapEnd > overlapStart)
                {
                    commands.Add(
                        new FrameRunCommand(
                            overlapEnd - overlapStart,
                            frame.HeldActions,
                            frame.AxisX,
                            frame.AxisY,
                            frame.HasAnalogAxes,
                            frame.Span));
                }

                cursor = frameEnd;
                if (cursor >= endTick)
                {
                    break;
                }
            }

            return new MovieDocument(
                sourceName,
                movie.Header,
                commands);
        }
    }
}
