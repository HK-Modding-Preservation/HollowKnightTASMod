using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace HollowKnightTAS.Runtime.Manifest
{
    public sealed class VerificationPreflightResult
    {
        public VerificationPreflightResult(
            bool requested,
            bool allowed,
            IEnumerable<string> unexpectedMods,
            string reason)
        {
            Requested = requested;
            Allowed = allowed;
            UnexpectedMods = new ReadOnlyCollection<string>(
                unexpectedMods
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList());
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }

        public bool Requested { get; }
        public bool Allowed { get; }
        public IReadOnlyList<string> UnexpectedMods { get; }
        public string Reason { get; }
    }

    public static class VerificationPreflight
    {
        public static VerificationPreflightResult Evaluate(bool requested)
        {
            // Keep the existing manifest fields, but do not restrict external Mods.
            // Loaded Mod identities are recorded separately by RuntimeEnvironmentReader.
            return new VerificationPreflightResult(
                requested,
                requested,
                Array.Empty<string>(),
                requested ? "Allowed" : "NotRequested");
        }
    }
}
