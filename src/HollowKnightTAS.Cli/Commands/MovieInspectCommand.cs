using System.Globalization;
using System.IO;
using System.Linq;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Cli.Commands
{
    internal static class MovieInspectCommand
    {
        public const string Usage =
            "Usage: HollowKnightTAS.Cli movie inspect <movie-path>";

        public static int Run(
            string[] args,
            TextWriter standardOutput,
            TextWriter standardError)
        {
            if (args.Length != 1)
            {
                standardError.WriteLine(Usage);
                return 2;
            }

            var loaded = MovieCommandUtilities.LoadAndValidate(
                args[0],
                MovieValidationContext.CreateDefault(),
                standardError);
            var document = loaded.Document;
            var writer = new MovieCanonicalWriter();

            standardOutput.WriteLine("movie-id=" + writer.ComputeMovieId(document));
            standardOutput.WriteLine(
                "protocol=" + document.Header.ProtocolVersion.ToString(
                    CultureInfo.InvariantCulture));
            standardOutput.WriteLine("game=" + document.Header.GameVersion);
            standardOutput.WriteLine("api=" + document.Header.ApiVersion);
            standardOutput.WriteLine(
                "manifest-sha256=" + document.Header.ManifestSha256);
            standardOutput.WriteLine(
                "baseline=" + document.Header.BaselineId + " "
                + document.Header.BaselineSha256);
            standardOutput.WriteLine("tick-unit=" + document.Header.TickUnit);
            standardOutput.WriteLine(
                "input-ticks=" + loaded.Report.ExpandedTickCount.ToString(
                    CultureInfo.InvariantCulture));
            standardOutput.WriteLine(
                "commands=" + document.Commands.Count.ToString(
                    CultureInfo.InvariantCulture));
            standardOutput.WriteLine(
                "markers=" + document.Commands.OfType<MarkerCommand>().Count().ToString(
                    CultureInfo.InvariantCulture));
            standardOutput.WriteLine(
                "checkpoints="
                + document.Commands.OfType<CheckpointCommand>().Count().ToString(
                    CultureInfo.InvariantCulture));
            standardOutput.WriteLine(
                "asserts=" + document.Commands.OfType<AssertCommand>().Count().ToString(
                    CultureInfo.InvariantCulture));
            return 0;
        }
    }
}
