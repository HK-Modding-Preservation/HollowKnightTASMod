using System;
using System.Collections.Generic;
using System.Threading;

namespace HollowKnightTAS.Runtime.Control
{
    /// <summary>
    /// Verification-only bridge from the frozen T24 observer's existing
    /// recording-arm ManualReset event to the Runtime completed-frame guard.
    /// It neither advances Unity nor writes gameplay state.
    /// </summary>
    public sealed class T24DeferredRecordingArmGate : IDisposable
    {
        public const string ArgumentPrefix =
            "--hktas-t24-deferred-recording-arm=";
        public const string EventPrefix =
            "HollowKnightTAS.T24.RecordingArm.";

        private EventWaitHandle? release;
        private bool consumed;

        public T24DeferredRecordingArmGate(string runId)
        {
            RunId = RequireRunId(runId);
            release = new EventWaitHandle(
                false,
                EventResetMode.ManualReset,
                EventPrefix + RunId);
        }

        public string RunId { get; }
        public bool ReleaseConsumed => consumed;

        public bool TryConsumeRelease()
        {
            var active = release
                         ?? throw new ObjectDisposedException(
                             nameof(T24DeferredRecordingArmGate));
            if (consumed || !active.WaitOne(0))
            {
                return false;
            }

            consumed = true;
            return true;
        }

        public void Dispose()
        {
            release?.Dispose();
            release = null;
        }

        public static bool TryParseRunId(
            IEnumerable<string> arguments,
            out string runId,
            out string error)
        {
            if (arguments == null)
            {
                throw new ArgumentNullException(nameof(arguments));
            }

            runId = string.Empty;
            error = string.Empty;
            var count = 0;
            foreach (var argument in arguments)
            {
                if (argument == null
                    || !argument.StartsWith(
                        ArgumentPrefix,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                count++;
                runId = argument.Substring(ArgumentPrefix.Length);
            }

            if (count == 0)
            {
                return true;
            }

            if (count != 1)
            {
                runId = string.Empty;
                error = "Exactly one deferred recording-arm argument is allowed.";
                return false;
            }

            try
            {
                runId = RequireRunId(runId);
                return true;
            }
            catch (ArgumentException exception)
            {
                runId = string.Empty;
                error = exception.Message;
                return false;
            }
        }

        private static string RequireRunId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.Length > 128)
            {
                throw new ArgumentException(
                    "The deferred recording-arm run ID must contain 1-128 characters.",
                    nameof(value));
            }

            foreach (var character in value)
            {
                if (!(character >= 'a' && character <= 'z')
                    && !(character >= 'A' && character <= 'Z')
                    && !(character >= '0' && character <= '9')
                    && character != '-'
                    && character != '_'
                    && character != '.')
                {
                    throw new ArgumentException(
                        "The deferred recording-arm run ID contains an invalid character.",
                        nameof(value));
                }
            }

            return value;
        }
    }
}
