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
        public static VerificationPreflightResult Evaluate(
            bool requested,
            IEnumerable<string> loadedMods,
            IEnumerable<string> allowedMods)
        {
            if (loadedMods == null)
            {
                throw new ArgumentNullException(nameof(loadedMods));
            }

            if (allowedMods == null)
            {
                throw new ArgumentNullException(nameof(allowedMods));
            }

            var allowed = new HashSet<string>(
                allowedMods.Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.Ordinal);
            var unexpected = loadedMods
                .Where(value => !string.IsNullOrWhiteSpace(value) && !allowed.Contains(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            if (!requested)
            {
                return new VerificationPreflightResult(
                    false,
                    false,
                    unexpected,
                    "NotRequested");
            }

            if (unexpected.Length > 0)
            {
                return new VerificationPreflightResult(
                    true,
                    false,
                    unexpected,
                    "UnexpectedMods");
            }

            return new VerificationPreflightResult(
                true,
                true,
                unexpected,
                "Allowed");
        }
    }
}

