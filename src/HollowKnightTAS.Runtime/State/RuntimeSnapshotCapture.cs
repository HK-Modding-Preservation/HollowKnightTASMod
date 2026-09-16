using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.State;

namespace HollowKnightTAS.Runtime.State
{
    public sealed class SnapshotCaptureResult
    {
        private SnapshotCaptureResult(
            TickStamp stamp,
            SemanticSnapshot? snapshot,
            byte[]? canonicalBytes,
            string? sha256,
            string? failedProbeId,
            string? error)
        {
            Stamp = stamp;
            Snapshot = snapshot;
            CanonicalBytes = canonicalBytes == null
                ? null
                : (byte[])canonicalBytes.Clone();
            Sha256 = sha256;
            FailedProbeId = failedProbeId;
            Error = error;
        }

        public bool Success => Snapshot != null;
        public TickStamp Stamp { get; }
        public SemanticSnapshot? Snapshot { get; }
        public byte[]? CanonicalBytes { get; }
        public string? Sha256 { get; }
        public string? FailedProbeId { get; }
        public string? Error { get; }

        internal static SnapshotCaptureResult Succeeded(
            TickStamp stamp,
            SemanticSnapshot snapshot,
            byte[] canonicalBytes,
            string sha256)
        {
            return new SnapshotCaptureResult(
                stamp,
                snapshot,
                canonicalBytes,
                sha256,
                null,
                null);
        }

        internal static SnapshotCaptureResult Failed(
            TickStamp stamp,
            string failedProbeId,
            string error)
        {
            return new SnapshotCaptureResult(
                stamp,
                null,
                null,
                null,
                failedProbeId,
                error);
        }
    }

    public sealed class RuntimeSnapshotCapture
    {
        private readonly ReadOnlyCollection<ISemanticProbe> probes;
        private readonly int schemaVersion;

        public RuntimeSnapshotCapture()
            : this(
                new ISemanticProbe[]
                {
                    new SceneProbe(),
                    new HeroProbe(),
                    new PlayerDataProbe()
                })
        {
        }

        public RuntimeSnapshotCapture(IEnumerable<ISemanticProbe> probes)
            : this(probes, SemanticSnapshotSchemaV1.Version)
        {
        }

        public RuntimeSnapshotCapture(IEnumerable<ISemanticProbe> probes, int schemaVersion)
        {
            if (!SemanticSnapshotSchemas.IsSupported(schemaVersion))
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            this.schemaVersion = schemaVersion;
            if (probes == null)
            {
                throw new ArgumentNullException(nameof(probes));
            }

            var copy = new List<ISemanticProbe>();
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var probe in probes)
            {
                if (probe == null)
                {
                    throw new ArgumentException(
                        "Semantic probe list cannot contain null.",
                        nameof(probes));
                }

                if (string.IsNullOrWhiteSpace(probe.ProbeId)
                    || !identifiers.Add(probe.ProbeId))
                {
                    throw new ArgumentException(
                        "Semantic probe IDs must be non-empty and unique.",
                        nameof(probes));
                }

                copy.Add(probe);
            }

            if (copy.Count == 0)
            {
                throw new ArgumentException(
                    "At least one semantic probe is required.",
                    nameof(probes));
            }

            this.probes = new ReadOnlyCollection<ISemanticProbe>(copy);
        }

        public SnapshotCaptureResult Capture(TickStamp stamp)
        {
            if (stamp.Phase != TickPhase.LateUpdateEnd)
            {
                return SnapshotCaptureResult.Failed(
                    stamp,
                    "capture-phase",
                    "Semantic Snapshot v1 requires LateUpdateEnd, received "
                    + stamp.Phase
                    + ".");
            }

            var builder = new SemanticSnapshotBuilder(schemaVersion);
            foreach (var probe in probes)
            {
                try
                {
                    probe.Capture(builder);
                }
                catch (Exception exception)
                {
                    return SnapshotCaptureResult.Failed(
                        stamp,
                        probe.ProbeId,
                        exception.GetType().FullName + ": " + exception.Message);
                }
            }

            try
            {
                var snapshot = builder.Build();
                var bytes = SemanticSnapshotCanonicalizer.Serialize(snapshot);
                var hash = SemanticSnapshotHasher.ComputeSha256(snapshot);
                return SnapshotCaptureResult.Succeeded(
                    stamp,
                    snapshot,
                    bytes,
                    hash);
            }
            catch (Exception exception)
            {
                return SnapshotCaptureResult.Failed(
                    stamp,
                    "snapshot-finalize",
                    exception.GetType().FullName + ": " + exception.Message);
            }
        }
    }
}
