using System;
using System.Globalization;
using System.IO;
using System.Text;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Serialization;
using HollowKnightTAS.Core.Verification;

namespace HollowKnightTAS.Cli.Commands
{
    internal static class CompareRunsCommand
    {
        public const string Usage =
            "Usage: HollowKnightTAS.Cli verification compare <expected-run.json> <actual-run.json> [--out <report.json>]";

        public static int Run(
            string[] args,
            TextWriter standardOutput,
            TextWriter standardError)
        {
            if (args.Length != 2 && args.Length != 4)
            {
                standardError.WriteLine(Usage);
                return 2;
            }

            string? outputPath = null;
            if (args.Length == 4)
            {
                if (!string.Equals(args[2], "--out", StringComparison.Ordinal))
                {
                    standardError.WriteLine(Usage);
                    return 2;
                }

                outputPath = Path.GetFullPath(args[3]);
            }

            var expected = VerificationEvidenceCodec.Load(args[0]);
            var actual = VerificationEvidenceCodec.Load(args[1]);
            var comparison = RunComparator.Compare(expected, actual);
            if (outputPath != null)
            {
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(
                    outputPath,
                    SerializeComparison(expected, actual, comparison),
                    new UTF8Encoding(false, true));
            }

            switch (comparison.Status)
            {
                case RunComparisonStatus.Match:
                    standardOutput.WriteLine(
                        "MATCH " + expected.RunSignature);
                    return 0;
                case RunComparisonStatus.Incomparable:
                    standardOutput.WriteLine(
                        "INCOMPARABLE " + comparison.Message);
                    return 5;
                case RunComparisonStatus.Desync:
                    standardOutput.WriteLine(
                        "DESYNC " + comparison.Message);
                    if (comparison.Desync != null)
                    {
                        foreach (var entry in comparison.Desync.SemanticDiff.Entries)
                        {
                            standardOutput.WriteLine(
                                "DIFF "
                                + entry.Key
                                + " expected="
                                + entry.ExpectedDisplay
                                + " actual="
                                + entry.ActualDisplay);
                        }
                    }

                    return 4;
                default:
                    throw new InvalidOperationException(
                        "Unknown run comparison status.");
            }
        }

        private static string SerializeComparison(
            RunEvidence expected,
            RunEvidence actual,
            RunComparison comparison)
        {
            var builder = new StringBuilder(4096);
            builder.Append('{');
            AppendNumber(builder, "schemaVersion", 1);
            AppendString(builder, "status", comparison.Status.ToString());
            AppendString(builder, "message", comparison.Message);
            AppendString(
                builder,
                "expectedManifestSha256",
                expected.ManifestSha256);
            AppendString(
                builder,
                "actualManifestSha256",
                actual.ManifestSha256);
            AppendString(
                builder,
                "expectedBaselineSha256",
                expected.BaselineSha256);
            AppendString(
                builder,
                "actualBaselineSha256",
                actual.BaselineSha256);
            AppendString(builder, "expectedMovieId", expected.MovieId);
            AppendString(builder, "actualMovieId", actual.MovieId);
            AppendString(
                builder,
                "expectedRunSignature",
                expected.RunSignature);
            AppendString(
                builder,
                "actualRunSignature",
                actual.RunSignature);
            AppendPropertyPrefix(builder, "firstDifference");
            if (comparison.Desync == null)
            {
                builder.Append("null");
            }
            else
            {
                var desync = comparison.Desync;
                builder.Append('{');
                AppendNumber(
                    builder,
                    "milestoneIndex",
                    desync.FirstMilestoneIndex);
                AppendString(
                    builder,
                    "milestoneId",
                    desync.FirstMilestoneId);
                AppendNumber(builder, "movieTick", desync.FirstMovieTick);
                AppendString(builder, "reason", desync.Reason);
                AppendString(
                    builder,
                    "expectedScene",
                    desync.Expected.SceneName);
                AppendString(
                    builder,
                    "actualScene",
                    desync.Actual.SceneName);
                AppendString(
                    builder,
                    "expectedRngStateSha256",
                    desync.Expected.RngStateSha256);
                AppendString(
                    builder,
                    "actualRngStateSha256",
                    desync.Actual.RngStateSha256);
                AppendPropertyPrefix(builder, "expectedContext");
                AppendMilestoneContext(builder, desync.Expected);
                AppendPropertyPrefix(builder, "actualContext");
                AppendMilestoneContext(builder, desync.Actual);
                AppendPropertyPrefix(builder, "semanticDiff");
                builder.Append('[');
                for (var index = 0;
                     index < desync.SemanticDiff.Entries.Count;
                     index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }

                    var entry = desync.SemanticDiff.Entries[index];
                    builder.Append('{');
                    AppendString(builder, "key", entry.Key);
                    AppendString(
                        builder,
                        "expectedBits",
                        entry.ExpectedBits);
                    AppendString(builder, "actualBits", entry.ActualBits);
                    AppendString(
                        builder,
                        "expectedDisplay",
                        entry.ExpectedDisplay);
                    AppendString(
                        builder,
                        "actualDisplay",
                        entry.ActualDisplay);
                    builder.Append('}');
                }

                builder.Append(']');
                builder.Append('}');
            }

            builder.Append('}');
            return builder.ToString();
        }

        private static void AppendMilestoneContext(
            StringBuilder builder,
            MilestoneRecord milestone)
        {
            builder.Append('{');
            AppendString(
                builder,
                "semanticSha256",
                milestone.SemanticSha256);
            AppendString(
                builder,
                "ledgerWindowSha256",
                milestone.LedgerWindowSha256);
            AppendString(builder, "sceneName", milestone.SceneName);
            AppendPropertyPrefix(builder, "tickStamp");
            AppendTickStamp(builder, milestone.TickStamp);
            AppendPropertyPrefix(builder, "input");
            AppendInput(builder, milestone.Input);
            AppendPropertyPrefix(builder, "ledgerWindow");
            builder.Append('[');
            for (var index = 0;
                 index < milestone.LedgerWindow.Count;
                 index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                AppendLedgerEntry(
                    builder,
                    milestone.LedgerWindow[index]);
            }

            builder.Append(']');
            builder.Append('}');
        }

        private static void AppendLedgerEntry(
            StringBuilder builder,
            VerificationLedgerEntry entry)
        {
            builder.Append('{');
            AppendNumber(builder, "movieTick", entry.MovieTick);
            AppendPropertyPrefix(builder, "tickStamp");
            AppendTickStamp(builder, entry.Stamp);
            AppendPropertyPrefix(builder, "input");
            AppendInput(builder, entry.Input);
            AppendNumber(
                builder,
                "fixedStepsSincePreviousVisual",
                entry.FixedStepsSincePreviousVisual);
            AppendNumber(
                builder,
                "timeMinusFixedTimeBits",
                entry.TimeMinusFixedTimeBits);
            AppendString(builder, "sceneName", entry.SceneName);
            AppendString(builder, "eventName", entry.EventName);
            AppendString(
                builder,
                "rngStateSha256",
                entry.RngStateSha256);
            builder.Append('}');
        }

        private static void AppendTickStamp(
            StringBuilder builder,
            TickStamp stamp)
        {
            builder.Append('{');
            AppendUnsigned(builder, "inputTick", stamp.InputTick);
            AppendNumber(builder, "visualTick", stamp.VisualTick);
            AppendNumber(builder, "fixedTick", stamp.FixedTick);
            AppendNumber(builder, "sceneEpoch", stamp.SceneEpoch);
            AppendString(builder, "phase", stamp.Phase.ToString());
            builder.Append('}');
        }

        private static void AppendInput(
            StringBuilder builder,
            InputSample input)
        {
            builder.Append('{');
            AppendUnsigned(builder, "inputTick", input.InputTick);
            AppendNumber(builder, "held", (int)input.Held);
            AppendNumber(builder, "pressed", (int)input.Pressed);
            AppendNumber(builder, "released", (int)input.Released);
            AppendNumber(builder, "axisX", input.AxisX);
            AppendNumber(builder, "axisY", input.AxisY);
            builder.Append('}');
        }

        private static void AppendString(
            StringBuilder builder,
            string name,
            string value)
        {
            AppendPropertyPrefix(builder, name);
            CanonicalJsonWriter.AppendString(builder, value);
        }

        private static void AppendNumber(
            StringBuilder builder,
            string name,
            long value)
        {
            AppendPropertyPrefix(builder, name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendUnsigned(
            StringBuilder builder,
            string name,
            ulong value)
        {
            AppendPropertyPrefix(builder, name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendPropertyPrefix(
            StringBuilder builder,
            string name)
        {
            if (builder[builder.Length - 1] != '{')
            {
                builder.Append(',');
            }

            CanonicalJsonWriter.AppendString(builder, name);
            builder.Append(':');
        }
    }
}
