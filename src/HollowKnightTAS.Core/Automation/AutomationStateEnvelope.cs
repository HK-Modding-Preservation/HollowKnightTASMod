using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace HollowKnightTAS.Core.Automation
{
    public sealed class AutomationStateEnvelope
    {
        public AutomationStateEnvelope(
            string sessionId,
            string manifestSha256,
            string runtimeMode,
            long movieTick,
            string tickPhase,
            DateTimeOffset capturedAtUtc,
            long ageMilliseconds,
            string semanticSnapshotSha256,
            IReadOnlyDictionary<string, string> fields,
            IReadOnlyList<string> activeCapabilities)
        {
            SessionId = RequireText(sessionId, nameof(sessionId));
            ManifestSha256 = RequireSha256(
                manifestSha256,
                nameof(manifestSha256));
            RuntimeMode = RequireText(runtimeMode, nameof(runtimeMode));
            if (movieTick < -1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(movieTick));
            }

            MovieTick = movieTick;
            TickPhase = RequireText(tickPhase, nameof(tickPhase));
            CapturedAtUtc = capturedAtUtc;
            if (ageMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ageMilliseconds));
            }

            AgeMilliseconds = ageMilliseconds;
            SemanticSnapshotSha256 = RequireSha256(
                semanticSnapshotSha256,
                nameof(semanticSnapshotSha256));
            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }

            if (activeCapabilities == null)
            {
                throw new ArgumentNullException(
                    nameof(activeCapabilities));
            }

            var copiedFields = new Dictionary<string, string>(
                StringComparer.Ordinal);
            foreach (var pair in fields)
            {
                copiedFields.Add(
                    RequireText(pair.Key, nameof(fields)),
                    pair.Value
                    ?? throw new ArgumentException(
                        "State field values cannot be null.",
                        nameof(fields)));
            }

            var copiedCapabilities = activeCapabilities
                .Select(
                    item => RequireText(
                        item,
                        nameof(activeCapabilities)))
                .ToArray();
            if (copiedCapabilities.Distinct(
                    StringComparer.Ordinal).Count()
                != copiedCapabilities.Length)
            {
                throw new ArgumentException(
                    "Active capabilities must be unique.",
                    nameof(activeCapabilities));
            }

            Fields = new ReadOnlyDictionary<string, string>(
                copiedFields);
            ActiveCapabilities = Array.AsReadOnly(
                copiedCapabilities);
        }

        public int SchemaVersion => AutomationProtocol.Version;
        public string SessionId { get; }
        public string ManifestSha256 { get; }
        public string RuntimeMode { get; }
        public long MovieTick { get; }
        public string TickPhase { get; }
        public DateTimeOffset CapturedAtUtc { get; }
        public long AgeMilliseconds { get; }
        public string SemanticSnapshotSha256 { get; }
        public IReadOnlyDictionary<string, string> Fields { get; }
        public IReadOnlyList<string> ActiveCapabilities { get; }

        private static string RequireText(
            string value,
            string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Value cannot be empty.",
                    name);
            }

            return value;
        }

        private static string RequireSha256(
            string value,
            string name)
        {
            RequireText(value, name);
            if (value.Length != 64
                || value.Any(
                    character =>
                        !(character >= '0' && character <= '9')
                        && !(character >= 'a'
                             && character <= 'f')))
            {
                throw new ArgumentException(
                    "SHA-256 must contain 64 lowercase hex characters.",
                    name);
            }

            return value;
        }
    }
}
