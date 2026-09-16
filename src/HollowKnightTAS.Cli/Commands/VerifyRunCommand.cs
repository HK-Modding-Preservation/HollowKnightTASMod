using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HollowKnightTAS.Core.Verification;

namespace HollowKnightTAS.Cli.Commands
{
    internal static class VerifyRunCommand
    {
        public const string ValidateUsage =
            "Usage: HollowKnightTAS.Cli verification validate <run.json>";
        public const string CampaignUsage =
            "Usage: HollowKnightTAS.Cli verification campaign <campaign-directory>";

        public static int Validate(
            string[] args,
            TextWriter standardOutput,
            TextWriter standardError)
        {
            if (args.Length != 1)
            {
                standardError.WriteLine(ValidateUsage);
                return 2;
            }

            var evidence = VerificationEvidenceCodec.Load(args[0]);
            standardOutput.WriteLine("VALID " + evidence.RunSignature);
            return 0;
        }

        public static int Campaign(
            string[] args,
            TextWriter standardOutput,
            TextWriter standardError)
        {
            if (args.Length != 1)
            {
                standardError.WriteLine(CampaignUsage);
                return 2;
            }

            var campaignDirectory = Path.GetFullPath(args[0]);
            if (!Directory.Exists(campaignDirectory))
            {
                throw new DirectoryNotFoundException(
                    "Campaign directory was not found: " + campaignDirectory);
            }

            var paths = Directory.GetDirectories(
                    campaignDirectory,
                    "run-*",
                    SearchOption.TopDirectoryOnly)
                .Select(path => Path.Combine(path, "run.json"))
                .Where(File.Exists)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (paths.Length == 0)
            {
                throw new InvalidDataException(
                    "Campaign contains no run-*/run.json evidence.");
            }

            var runs = paths
                .Select(VerificationEvidenceCodec.Load)
                .ToArray();
            RequireIndependentProcesses(runs);
            var expected = runs[0];
            for (var index = 1; index < runs.Length; index++)
            {
                var comparison = RunComparator.Compare(expected, runs[index]);
                if (comparison.Status == RunComparisonStatus.Incomparable)
                {
                    standardOutput.WriteLine(
                        "INCOMPARABLE run="
                        + (index + 1)
                        + " "
                        + comparison.Message);
                    return 5;
                }

                if (comparison.Status == RunComparisonStatus.Desync)
                {
                    standardOutput.WriteLine(
                        "DESYNC run="
                        + (index + 1)
                        + " "
                        + comparison.Message);
                    return 4;
                }
            }

            var level = runs.Length >= 10
                ? "LOCAL_VERIFIED"
                : "MATCHING_UNLABELED";
            standardOutput.WriteLine(
                level
                + " runs="
                + runs.Length
                + " signature="
                + expected.RunSignature);
            return 0;
        }

        private static void RequireIndependentProcesses(
            IReadOnlyList<RunEvidence> runs)
        {
            var sessions = new HashSet<string>(StringComparer.Ordinal);
            var processes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var run in runs)
            {
                if (!sessions.Add(run.SessionId))
                {
                    throw new InvalidDataException(
                        "Campaign reuses a sessionId.");
                }

                if (!processes.Add(run.ProcessInstanceId))
                {
                    throw new InvalidDataException(
                        "Campaign reuses a processInstanceId.");
                }
            }
        }
    }
}
