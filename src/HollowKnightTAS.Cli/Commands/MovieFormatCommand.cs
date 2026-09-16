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

            var loaded = MovieCommandUtilities.LoadAndValidate(
                args[0],
                MovieValidationContext.CreateDefault(),
                standardError);
            var writer = new MovieCanonicalWriter();
            var canonicalBytes = writer.WriteUtf8(loaded.Document);
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
                    "FORMATTED " + writer.ComputeMovieId(loaded.Document));
                return 0;
            }

            standardOutput.Write(writer.WriteToString(loaded.Document));
            return 0;
        }
    }
}
