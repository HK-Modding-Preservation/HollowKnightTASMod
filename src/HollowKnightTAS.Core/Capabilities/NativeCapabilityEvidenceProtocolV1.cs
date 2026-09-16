using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HollowKnightTAS.Core.Ipc;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Core.Capabilities
{
    public static class NativeCapabilityEvidenceProtocolV1
    {
        private static readonly string[] RequiredFields =
        {
            "assemblyCSharpSha256",
            "attachCyclesCompleted",
            "buildWhitelistId",
            "capabilityId",
            "capabilityVersion",
            "checkpointStatus",
            "coreAssemblySha256",
            "environmentManifestSha256",
            "evidenceVersion",
            "fallback",
            "imageSha256",
            "moduleMapSha256",
            "parentProcessVerified",
            "pssCaptureApiAvailable",
            "rawPagesPersisted",
            "requestId",
            "runtimeAssemblySha256",
            "status",
            "targetFingerprint",
            "threadSetSha256"
        };

        public static void Validate(
            IReadOnlyDictionary<string, string> fields)
        {
            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }
            if (fields.TryGetValue("status", out var status)
                && string.Equals(
                    status,
                    "faulted",
                    StringComparison.Ordinal))
            {
                ValidateFault(fields);
                return;
            }
            if (fields.TryGetValue("status", out status)
                && string.Equals(
                    status,
                    "started",
                    StringComparison.Ordinal))
            {
                ValidateStarted(fields);
                return;
            }
            if (fields.Count != RequiredFields.Length
                || RequiredFields.Any(
                    field => !fields.ContainsKey(field)))
            {
                throw new InvalidDataException(
                    "Native evidence payload has unexpected fields.");
            }
            if (!IpcIdentifier.IsValid(fields["requestId"], 128))
            {
                throw new InvalidDataException(
                    "Native evidence requestId is invalid.");
            }
            if (!string.Equals(
                    fields["status"],
                    "verified",
                    StringComparison.Ordinal)
                || !string.Equals(
                    fields["capabilityId"],
                    NativeCapabilityCatalog.ProcessObserve,
                    StringComparison.Ordinal)
                || !string.Equals(
                    fields["capabilityVersion"],
                    "1",
                    StringComparison.Ordinal)
                || !string.Equals(
                    fields["evidenceVersion"],
                    "1",
                    StringComparison.Ordinal)
                || !string.Equals(
                    fields["fallback"],
                    "none",
                    StringComparison.Ordinal)
                || !string.Equals(
                    fields["attachCyclesCompleted"],
                    "100",
                    StringComparison.Ordinal)
                || !string.Equals(
                    fields["parentProcessVerified"],
                    "true",
                    StringComparison.Ordinal)
                || !string.Equals(
                    fields["rawPagesPersisted"],
                    "false",
                    StringComparison.Ordinal)
                || !string.Equals(
                    fields["checkpointStatus"],
                    "unsupported",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Native evidence safety claims are invalid.");
            }
            if (!string.Equals(
                    fields["pssCaptureApiAvailable"],
                    "true",
                    StringComparison.Ordinal)
                && !string.Equals(
                    fields["pssCaptureApiAvailable"],
                    "false",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Native evidence PSS availability is invalid.");
            }
            if (!MovieProtocolV1.IsLowerSha256(
                    fields["targetFingerprint"])
                || !MovieProtocolV1.IsLowerSha256(
                    fields["imageSha256"])
                || !MovieProtocolV1.IsLowerSha256(
                    fields["assemblyCSharpSha256"])
                || !MovieProtocolV1.IsLowerSha256(
                    fields["coreAssemblySha256"])
                || !MovieProtocolV1.IsLowerSha256(
                    fields["environmentManifestSha256"])
                || !MovieProtocolV1.IsLowerSha256(
                    fields["runtimeAssemblySha256"])
                || !MovieProtocolV1.IsLowerSha256(
                    fields["moduleMapSha256"])
                || !MovieProtocolV1.IsLowerSha256(
                    fields["threadSetSha256"]))
            {
                throw new InvalidDataException(
                    "Native evidence hashes must be lowercase SHA-256.");
            }
            if (!IpcIdentifier.IsValid(
                    fields["buildWhitelistId"],
                    128))
            {
                throw new InvalidDataException(
                    "Native evidence build whitelist ID is invalid.");
            }
        }

        private static void ValidateFault(
            IReadOnlyDictionary<string, string> fields)
        {
            var expected = new[]
            {
                "errorCode",
                "fallback",
                "requestId",
                "status"
            };
            if (fields.Count != expected.Length
                || expected.Any(
                    field => !fields.ContainsKey(field))
                || !IpcIdentifier.IsValid(
                    fields["requestId"],
                    128)
                || !IpcIdentifier.IsValid(
                    fields["errorCode"],
                    128)
                || !string.Equals(
                    fields["fallback"],
                    "runtime-t09",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Native evidence fault payload is invalid.");
            }
        }

        private static void ValidateStarted(
            IReadOnlyDictionary<string, string> fields)
        {
            var expected = new[]
            {
                "capabilityId",
                "fallback",
                "requestId",
                "status"
            };
            if (fields.Count != expected.Length
                || expected.Any(
                    field => !fields.ContainsKey(field))
                || !IpcIdentifier.IsValid(
                    fields["requestId"],
                    128)
                || !string.Equals(
                    fields["capabilityId"],
                    NativeCapabilityCatalog.ProcessObserve,
                    StringComparison.Ordinal)
                || !string.Equals(
                    fields["fallback"],
                    "runtime-t09",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Native evidence start payload is invalid.");
            }
        }
    }
}
