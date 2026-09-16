using System;
using System.Collections.Generic;
using System.Linq;

namespace HollowKnightTAS.Companion.Services
{
    public sealed class CapabilityBroker
    {
        private readonly SortedDictionary<string, string> catalog =
            new SortedDictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["companion.studio"] = "available",
                ["ipc.runtime-v1"] = "available",
                ["native-host"] = "signed-sidecar",
                ["native.process.observe.v1"] =
                    "available-on-request",
                ["native.checkpoint.experimental.v1"] =
                    "unsupported",
                ["restore.full-replay"] = "available",
                ["restore.keyframe-tail"] = "not-advertised"
            };

        public IReadOnlyList<string> Snapshot()
        {
            return catalog.Select(
                    pair => pair.Key + " = " + pair.Value)
                .ToArray();
        }

        public void ApplyRuntimeCatalog(string value)
        {
            catalog["runtime.catalog"] =
                string.IsNullOrEmpty(value)
                    ? "empty"
                    : value;
        }

        public void ApplyNativeEvidence(
            IReadOnlyDictionary<string, string> fields)
        {
            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }
            catalog["native.process.observe.v1"] =
                fields.TryGetValue(
                    "processObserveStatus",
                    out var status)
                    ? status
                    : "faulted";
            catalog["native.target-fingerprint"] =
                fields.TryGetValue(
                    "targetFingerprint",
                    out var fingerprint)
                    ? fingerprint
                    : "unavailable";
            catalog["native.checkpoint.experimental.v1"] =
                "unsupported";
        }
    }
}
