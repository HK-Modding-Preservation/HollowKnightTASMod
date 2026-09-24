using System;
using System.Globalization;
using System.IO;
using System.Linq;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Cli.Commands
{
    internal static class MovieValidateCommand
    {
        public const string Usage =
            "Usage: HollowKnightTAS.Cli movie validate <movie-path> "
            + "[--manifest-sha256 <hash>] [--baseline-sha256 <hash|none>] "
            + "[--max-expanded-ticks <positive-int>]";

        public static int Run(
            string[] args,
            TextWriter standardOutput,
            TextWriter standardError)
        {
            if (!TryParseOptions(
                    args,
                    standardError,
                    out var path,
                    out var context))
            {
                return 2;
            }

            var v2Limit = args.Contains("--max-expanded-ticks", StringComparer.Ordinal)
                ? Math.Min(context.MaxExpandedTicks, MovieProtocolV2.MaximumExpandedFrames)
                : MovieProtocolV2.MaximumExpandedFrames;
            var loaded = MovieCommandUtilities.LoadAndValidateAny(
                path,
                context,
                new MovieV2ValidationContext(v2Limit,
                    MovieProtocolV2.MaximumSamplesPerFrame),
                standardError);
            if (loaded.V2Document != null)
            {
                if (args.Contains("--manifest-sha256", StringComparer.Ordinal)
                    || args.Contains("--baseline-sha256", StringComparer.Ordinal))
                {
                    standardError.WriteLine("INVALID v2 movies do not bind a save baseline; manifest options apply to v1 only.");
                    return 2;
                }
                var codec = new MovieV2Codec();
                standardOutput.WriteLine("VALID " + codec.ComputeMovieId(loaded.V2Document)
                    + " frames=" + loaded.ExpandedFrames.ToString(CultureInfo.InvariantCulture)
                    + " frame-origin=startup0 tick-unit=input-playerloop");
                return 0;
            }
            var writer = new MovieCanonicalWriter();
            standardOutput.WriteLine(
                "VALID "
                + writer.ComputeMovieId(loaded.V1Document!)
                + " ticks="
                + loaded.ExpandedFrames.ToString(CultureInfo.InvariantCulture)
                + " commands="
                + loaded.V1Document!.Commands.Count.ToString(CultureInfo.InvariantCulture));
            return 0;
        }

        private static bool TryParseOptions(
            string[] args,
            TextWriter standardError,
            out string path,
            out MovieValidationContext context)
        {
            path = string.Empty;
            context = MovieValidationContext.CreateDefault();
            if (args.Length < 1 || args[0].StartsWith("--", StringComparison.Ordinal))
            {
                standardError.WriteLine(Usage);
                return false;
            }

            path = args[0];
            string? expectedManifest = null;
            string? expectedBaseline = null;
            var maxTicks = MovieProtocolV1.DefaultMaxExpandedTicks;
            var seenManifest = false;
            var seenBaseline = false;
            var seenLimit = false;

            for (var index = 1; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length)
                {
                    standardError.WriteLine(Usage);
                    return false;
                }

                var option = args[index];
                var value = args[index + 1];
                switch (option)
                {
                    case "--manifest-sha256":
                        if (seenManifest || !MovieProtocolV1.IsLowerSha256(value))
                        {
                            standardError.WriteLine(Usage);
                            return false;
                        }

                        seenManifest = true;
                        expectedManifest = value;
                        break;
                    case "--baseline-sha256":
                        if (seenBaseline
                            || !(string.Equals(value, "none", StringComparison.Ordinal)
                                 || MovieProtocolV1.IsLowerSha256(value)))
                        {
                            standardError.WriteLine(Usage);
                            return false;
                        }

                        seenBaseline = true;
                        expectedBaseline = value;
                        break;
                    case "--max-expanded-ticks":
                        if (seenLimit
                            || !long.TryParse(
                                value,
                                NumberStyles.None,
                                CultureInfo.InvariantCulture,
                                out maxTicks)
                            || maxTicks <= 0)
                        {
                            standardError.WriteLine(Usage);
                            return false;
                        }

                        seenLimit = true;
                        break;
                    default:
                        standardError.WriteLine(Usage);
                        return false;
                }
            }

            context = new MovieValidationContext(
                maxTicks,
                MovieProtocolV1.DefaultSemanticPaths,
                expectedManifest,
                expectedBaseline);
            return true;
        }
    }
}
