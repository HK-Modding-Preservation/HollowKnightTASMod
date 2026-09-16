using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HollowKnightTAS.Core.Keyframes;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Runtime.Keyframes
{
    public sealed class KeyframeTierResolution
    {
        public KeyframeTierResolution(
            bool requested,
            KeyframeSupportTier tier,
            IEnumerable<string> reasonCodes,
            bool acceleratorRegistered = false)
        {
            if (!Enum.IsDefined(
                    typeof(KeyframeSupportTier),
                    tier))
            {
                throw new ArgumentOutOfRangeException(nameof(tier));
            }
            Requested = requested;
            Tier = tier;
            if (acceleratorRegistered && !requested)
            {
                throw new ArgumentException(
                    "A disabled capability cannot be registered.",
                    nameof(acceleratorRegistered));
            }
            AcceleratorRegistered = acceleratorRegistered;
            ReasonCodes = new ReadOnlyCollection<string>(
                (reasonCodes
                 ?? throw new ArgumentNullException(
                     nameof(reasonCodes)))
                .Select(
                    value => RequireReason(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
            if (ReasonCodes.Count == 0)
            {
                throw new ArgumentException(
                    "A keyframe tier resolution requires evidence.",
                    nameof(reasonCodes));
            }
        }

        public bool Requested { get; }
        public KeyframeSupportTier Tier { get; }
        public IReadOnlyList<string> ReasonCodes { get; }
        public bool CaptureAllowed =>
            Tier != KeyframeSupportTier.ReplayOnly;
        public bool AcceleratorRegistered { get; }

        public KeyframeTierResolution WithAcceleratorRegistration(
            bool registered)
        {
            var reasons = ReasonCodes;
            if (Requested && !registered)
            {
                reasons = new ReadOnlyCollection<string>(
                    ReasonCodes
                        .Concat(
                            new[]
                            {
                                "t09-restore-service-unavailable"
                            })
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(
                            value => value,
                            StringComparer.Ordinal)
                        .ToArray());
            }
            return new KeyframeTierResolution(
                Requested,
                Tier,
                reasons,
                registered);
        }

        private static string RequireReason(string value)
        {
            if (!MovieProtocolV1.IsIdentifier(value)
                || value.Length > 128)
            {
                throw new ArgumentException(
                    "A canonical reason code is required.",
                    nameof(value));
            }
            return value;
        }
    }

    public static class KeyframeEligibilityProbe
    {
        public static KeyframeTierResolution Resolve(
            bool requested,
            string rngCoverage,
            IEnumerable<string> unexpectedMods)
        {
            if (!requested)
            {
                return new KeyframeTierResolution(
                    false,
                    KeyframeSupportTier.ReplayOnly,
                    new[] { "disabled-by-setting" });
            }

            var reasons = new List<string>
            {
                "no-verified-room-entry-gates"
            };
            if (string.IsNullOrWhiteSpace(rngCoverage)
                || rngCoverage.IndexOf(
                    "partial",
                    StringComparison.OrdinalIgnoreCase) >= 0
                || rngCoverage.IndexOf(
                    "mismatch",
                    StringComparison.OrdinalIgnoreCase) >= 0
                || rngCoverage.IndexOf(
                    "disabled",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                reasons.Add("rng-coverage-not-complete");
            }
            if ((unexpectedMods ?? Array.Empty<string>()).Any())
            {
                reasons.Add("unregistered-runtime-mod-profile");
            }

            // RoomEntry is intentionally unreachable until a gate has its
            // own parity, fallback, and 600-tick oracle evidence.
            return new KeyframeTierResolution(
                true,
                KeyframeSupportTier.ReplayOnly,
                reasons);
        }
    }
}
