using System;
using System.Collections.Generic;

namespace HollowKnightTAS.Core.Movie
{
    public static class MovieV2Prefix
    {
        public static MovieV2Document Take(MovieV2Document movie, long count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            var runs = new List<NativeFrameRun>();
            foreach (var run in movie.Runs)
            {
                if (count == 0) break;
                var length = Math.Min(count, run.RepeatCount);
                runs.Add(new NativeFrameRun(length, run.Samples, run.Span, run.FramesPerSecond, run.Authored, run.RngSeed));
                count -= length;
            }
            if (count != 0) throw new ArgumentOutOfRangeException(nameof(count));
            return new MovieV2Document(movie.SourceName, movie.Header, runs);
        }

        public static bool Matches(MovieV2Document original, MovieV2Document updated, long frame)
        {
            var codec = new MovieV2Codec();
            return codec.WriteCanonical(Take(original, frame)) == codec.WriteCanonical(Take(updated, frame));
        }
    }
}
