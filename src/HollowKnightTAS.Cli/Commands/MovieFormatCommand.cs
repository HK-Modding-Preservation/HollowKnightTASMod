using System;
using System.IO;
using System.Linq;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Cli.Commands
{
    internal static class MovieFormatCommand
    {
        public const string Usage =
            "Usage: HollowKnightTAS.Cli movie format <movie-path> [--check]";

        public static int Run(
            string[] args,
            TextWriter standardOutput,
            TextWriter standardError)
        {
            if (args.Length < 1
                || args.Length > 2
                || (args.Length == 2
                    && !string.Equals(args[1], "--check", StringComparison.Ordinal)))
            {
                standardError.WriteLine(Usage);
                return 2;
            }

            var loaded = MovieCommandUtilities.LoadAndValidateAny(
                args[0],
                MovieValidationContext.CreateDefault(),
                MovieV2ValidationContext.CreateDefault(),
                standardError);
            var canonicalText = loaded.V2Document != null
                ? new MovieV2Codec().WriteCanonical(loaded.V2Document)
                : new MovieCanonicalWriter().WriteToString(loaded.V1Document!);
            var canonicalBytes = new System.Text.UTF8Encoding(false).GetBytes(canonicalText);
            var movieId = loaded.V2Document != null
                ? new MovieV2Codec().ComputeMovieId(loaded.V2Document)
                : new MovieCanonicalWriter().ComputeMovieId(loaded.V1Document!);
            if (args.Length == 2)
            {
                if (!loaded.Bytes.SequenceEqual(canonicalBytes))
                {
                    standardError.WriteLine(
                        "NOT_FORMATTED "
                        + MovieCommandUtilities.Sanitize(loaded.Path)
                        + ": canonical bytes differ; run 'movie format' and save stdout.");
                    return 3;
                }

                standardOutput.WriteLine(
                    "FORMATTED " + movieId);
                return 0;
            }

            standardOutput.Write(canonicalText);
            return 0;
        }
    }
}
