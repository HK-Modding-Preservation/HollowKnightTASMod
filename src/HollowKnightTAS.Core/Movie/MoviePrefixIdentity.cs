using System;
using System.Linq;

namespace HollowKnightTAS.Core.Movie
{
    public static class MoviePrefixIdentity
    {
        public static string ComputeSha256(
            MovieDocument movie,
            long inputTickCount)
        {
            if (movie == null)
            {
                throw new ArgumentNullException(nameof(movie));
            }

            var expandedTicks = movie.Commands
                .OfType<FrameRunCommand>()
                .Aggregate(
                    0L,
                    (total, command) => checked(
                        total + command.FrameCount));
            if (inputTickCount < 0 || inputTickCount > expandedTicks)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(inputTickCount));
            }

            var prefix = inputTickCount == expandedTicks
                ? movie
                : MovieTimelineEditor.Delete(
                    movie,
                    inputTickCount,
                    expandedTicks - inputTickCount).Movie;
            return new MovieCanonicalWriter().ComputeMovieId(prefix);
        }

        public static bool IsCompatible(
            MovieDocument left,
            MovieDocument right,
            long inputTickCount)
        {
            return string.Equals(
                ComputeSha256(left, inputTickCount),
                ComputeSha256(right, inputTickCount),
                StringComparison.Ordinal);
        }
    }
}
