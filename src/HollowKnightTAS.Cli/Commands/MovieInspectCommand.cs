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

            var loaded = MovieCommandUtilities.LoadAndValidateAny(
                args[0],
                MovieValidationContext.CreateDefault(),
                MovieV2ValidationContext.CreateDefault(),
                standardError);
            if (loaded.V2Document != null)
            {
                var v2 = loaded.V2Document;
                standardOutput.WriteLine("movie-id=" + new MovieV2Codec().ComputeMovieId(v2));
                standardOutput.WriteLine("protocol=2");
                standardOutput.WriteLine("frame-origin=startup0");
                standardOutput.WriteLine("tick-unit=input-playerloop");
                standardOutput.WriteLine("game=" + v2.Header.GameVersion);
                standardOutput.WriteLine("api=" + v2.Header.ApiVersion);
                standardOutput.WriteLine("mod=" + v2.Header.ModVersion);
                standardOutput.WriteLine("native-profile=" + v2.Header.NativeProfileId);
                standardOutput.WriteLine("action-schema=" + v2.Header.ActionSchemaId);
                standardOutput.WriteLine("mouse-enabled=" + (v2.Header.MouseEnabled ? "true" : "false"));
                standardOutput.WriteLine("environment-sha256=" + v2.Header.EnvironmentSha256);
                standardOutput.WriteLine("viewport=" + v2.Header.ViewportWidth
                    + "x" + v2.Header.ViewportHeight);
                standardOutput.WriteLine("native-frames=" + loaded.ExpandedFrames.ToString(CultureInfo.InvariantCulture));
                standardOutput.WriteLine("runs=" + v2.Runs.Count.ToString(CultureInfo.InvariantCulture));
                standardOutput.WriteLine("save-binding=none");
                return 0;
            }
            var document = loaded.V1Document!;
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
                "input-ticks=" + loaded.ExpandedFrames.ToString(
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
