using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace HollowKnightTAS.ReferenceObserver
{
    internal sealed class ReferenceObserverOptions
    {
        private ReferenceObserverOptions(
            string runId,
            string mode,
            string outputDirectory,
            int maxTicks,
            bool exitWhenComplete,
            bool requireExternalInputSync)
        {
            RunId = runId;
            Mode = mode;
            OutputDirectory = outputDirectory;
            MaxTicks = maxTicks;
            ExitWhenComplete = exitWhenComplete;
            RequireExternalInputSync = requireExternalInputSync;
        }

        public string RunId { get; }
        public string Mode { get; }
        public string OutputDirectory { get; }
        public int MaxTicks { get; }
        public bool ExitWhenComplete { get; }
        public bool RequireExternalInputSync { get; }

        public static ReferenceObserverOptions? Parse(string[] arguments)
        {
            // The run ID is also the production ClockStartup binding. It is
            // not by itself an opt-in to the test observer or its exports.
            var observerRequested = false;
            foreach (var argument in arguments)
            {
                if (argument.StartsWith("--hktas-reference-", StringComparison.Ordinal)
                    && !argument.StartsWith("--hktas-reference-run=", StringComparison.Ordinal))
                    observerRequested = true;
            }
            if (!observerRequested) return null;
            var runId = Read(arguments, "--hktas-reference-run=");
            if (runId == null)
            {
                throw new InvalidDataException("Reference observer run ID is required.");
            }
            if (!IsIdentifier(runId))
            {
                throw new InvalidDataException(
                    "Reference observer run ID is invalid.");
            }

            var mode = Read(arguments, "--hktas-reference-mode=")
                       ?? throw new InvalidDataException(
                           "Reference observer mode is required.");
            switch (mode)
            {
                case "vanilla-reference":
                case "tas-passive":
                case "tas-continuous":
                case "tas-sequential":
                case "tas-batch":
                case "tas-manual-ui":
                    break;
                default:
                    throw new InvalidDataException(
                        "Reference observer mode is invalid.");
            }

            var encodedOutput = Read(
                                    arguments,
                                    "--hktas-reference-output-base64=")
                                ?? throw new InvalidDataException(
                                    "Reference observer output is required.");
            string outputDirectory;
            try
            {
                outputDirectory = Path.GetFullPath(
                    Encoding.UTF8.GetString(
                        Convert.FromBase64String(encodedOutput)));
            }
            catch (Exception exception)
                when (exception is FormatException
                      || exception is ArgumentException
                      || exception is NotSupportedException)
            {
                throw new InvalidDataException(
                    "Reference observer output path is invalid.",
                    exception);
            }

            var maxTicksText = Read(
                                   arguments,
                                   "--hktas-reference-max-ticks=")
                               ?? throw new InvalidDataException(
                                   "Reference observer max ticks are required.");
            if (!int.TryParse(
                    maxTicksText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var maxTicks)
                || maxTicks < 1
                || maxTicks > 10000)
            {
                throw new InvalidDataException(
                    "Reference observer max ticks must be in [1,10000].");
            }

            return new ReferenceObserverOptions(
                runId,
                mode,
                outputDirectory,
                maxTicks,
                Array.IndexOf(
                    arguments,
                    "--hktas-reference-exit") >= 0,
                Array.IndexOf(
                    arguments,
                    "--hktas-reference-require-external-input-sync") >= 0);
        }

        private static string? Read(string[] arguments, string prefix)
        {
            foreach (var argument in arguments)
            {
                if (argument.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return argument.Substring(prefix.Length);
                }
            }
            return null;
        }

        private static bool IsIdentifier(string value)
        {
            if (value.Length < 1 || value.Length > 96)
            {
                return false;
            }
            foreach (var character in value)
            {
                if (!(character >= 'a' && character <= 'z')
                    && !(character >= 'A' && character <= 'Z')
                    && !(character >= '0' && character <= '9')
                    && character != '-'
                    && character != '_')
                {
                    return false;
                }
            }
            return true;
        }
    }
}
