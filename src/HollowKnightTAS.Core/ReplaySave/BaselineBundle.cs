using System;
using System.IO;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.State;

namespace HollowKnightTAS.Core.ReplaySave
{
    public sealed class BaselineBundle
    {
        public const int CurrentSchemaVersion = 2;
        public const int MaximumSaveBytes = 64 * 1024 * 1024;
        public const int MaximumModdedSaveBytes = 16 * 1024 * 1024;
        public const int MaximumBundleBytes =
            MaximumSaveBytes
            + MaximumModdedSaveBytes
            + SemanticSnapshotCanonicalizer.MaximumCanonicalBytes
            + 4096;

        private readonly byte[] saveData;
        private readonly byte[]? moddedSaveData;
        private readonly byte[] semanticSnapshotBytes;

        public BaselineBundle(
            int schemaVersion,
            string baselineId,
            string baselineSemanticSha256,
            int originalSlot,
            DateTimeOffset capturedAtUtc,
            byte[] saveData,
            byte[]? moddedSaveData,
            byte[] semanticSnapshotBytes,
            RecordingOrigin? recordingOrigin = null)
        {
            if (schemaVersion < 1 || schemaVersion > CurrentSchemaVersion)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            SchemaVersion = schemaVersion;
            if (schemaVersion == 1 && recordingOrigin != null)
                throw new ArgumentException("Schema 1 cannot carry recording origin metadata.", nameof(recordingOrigin));
            RecordingOrigin = recordingOrigin;
            BaselineId = ReplaySaveDescriptor.RequireIdentifier(
                baselineId,
                nameof(baselineId));
            BaselineSemanticSha256 = ReplaySaveDescriptor.RequireSha256(
                baselineSemanticSha256,
                nameof(baselineSemanticSha256));
            if (originalSlot <= 0 || originalSlot > 99)
            {
                throw new ArgumentOutOfRangeException(nameof(originalSlot));
            }

            OriginalSlot = originalSlot;
            CapturedAtUtc = capturedAtUtc.ToUniversalTime();
            this.saveData = CopyBounded(
                saveData,
                MaximumSaveBytes,
                false,
                nameof(saveData));
            this.moddedSaveData = moddedSaveData == null
                ? null
                : CopyBounded(
                    moddedSaveData,
                    MaximumModdedSaveBytes,
                    true,
                    nameof(moddedSaveData));
            this.semanticSnapshotBytes = CopyBounded(
                semanticSnapshotBytes,
                SemanticSnapshotCanonicalizer.MaximumCanonicalBytes,
                false,
                nameof(semanticSnapshotBytes));

            var snapshot = SemanticSnapshotCanonicalizer.Deserialize(
                this.semanticSnapshotBytes);
            var actualSemantic = SemanticSnapshotHasher.ComputeSha256(snapshot);
            if (!string.Equals(
                    actualSemantic,
                    BaselineSemanticSha256,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Baseline semantic snapshot hash does not match its bytes.",
                    nameof(baselineSemanticSha256));
            }

            SaveDataSha256 = Sha256Utility.ComputeHex(this.saveData);
            ModdedSaveDataSha256 = this.moddedSaveData == null
                ? null
                : Sha256Utility.ComputeHex(this.moddedSaveData);
        }

        public int SchemaVersion { get; }
        public RecordingOrigin? RecordingOrigin { get; }
        public string BaselineId { get; }
        public string BaselineSemanticSha256 { get; }
        public int OriginalSlot { get; }
        public DateTimeOffset CapturedAtUtc { get; }
        public string SaveDataSha256 { get; }
        public string? ModdedSaveDataSha256 { get; }
        public byte[] SaveData => (byte[])saveData.Clone();
        public byte[]? ModdedSaveData =>
            moddedSaveData == null ? null : (byte[])moddedSaveData.Clone();
        public byte[] SemanticSnapshotBytes =>
            (byte[])semanticSnapshotBytes.Clone();

        public BaselineBundle WithRecordingOrigin(RecordingOrigin origin) =>
            new BaselineBundle(CurrentSchemaVersion, BaselineId,
                BaselineSemanticSha256, OriginalSlot, CapturedAtUtc,
                saveData, moddedSaveData, semanticSnapshotBytes,
                origin ?? throw new ArgumentNullException(nameof(origin)));

        private static byte[] CopyBounded(
            byte[] value,
            int maximumBytes,
            bool allowEmpty,
            string name)
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }

            if ((!allowEmpty && value.Length == 0)
                || value.Length > maximumBytes)
            {
                throw new ArgumentOutOfRangeException(name);
            }

            return (byte[])value.Clone();
        }
    }

    public static class BaselineBundleCodec
    {
        private const string Magic = "HKTB";

        public static byte[] Serialize(BaselineBundle value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            using (var writer = new ReplaySaveBinaryWriter())
            {
                writer.WriteMagic(Magic);
                writer.WriteInt32(value.SchemaVersion);
                writer.WriteString(value.BaselineId, 256);
                writer.WriteString(value.BaselineSemanticSha256, 64);
                writer.WriteInt32(value.OriginalSlot);
                writer.WriteInt64(value.CapturedAtUtc.UtcDateTime.Ticks);
                writer.WriteBytes(value.SaveData, BaselineBundle.MaximumSaveBytes);
                var modded = value.ModdedSaveData;
                writer.WriteByte(modded == null ? (byte)0 : (byte)1);
                if (modded != null)
                {
                    writer.WriteBytes(
                        modded,
                        BaselineBundle.MaximumModdedSaveBytes);
                }

                writer.WriteBytes(
                    value.SemanticSnapshotBytes,
                    SemanticSnapshotCanonicalizer.MaximumCanonicalBytes);
                if (value.SchemaVersion >= 2)
                {
                    var origin = value.RecordingOrigin;
                    writer.WriteByte(origin == null ? (byte)0 : (byte)1);
                    if (origin != null)
                    {
                        writer.WriteString(origin.ProfileId, 256);
                        writer.WriteInt64(BitConverter.DoubleToInt64Bits(origin.RootBoundarySeconds));
                        writer.WriteInt32(origin.FramesAfterRootBoundary);
                        writer.WriteInt64(BitConverter.DoubleToInt64Bits(origin.GameTimeSeconds));
                        writer.WriteInt64(BitConverter.DoubleToInt64Bits(origin.FixedTimeSeconds));
                    }
                }
                return writer.ToArray();
            }
        }

        public static BaselineBundle Deserialize(byte[] bytes)
        {
            var reader = new ReplaySaveBinaryReader(
                bytes,
                BaselineBundle.MaximumBundleBytes);
            reader.RequireMagic(Magic);
            var schema = reader.ReadInt32();
            if (schema < 1 || schema > BaselineBundle.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    "Unsupported baseline bundle schema: " + schema + ".");
            }

            var baselineId = reader.ReadString(256, "baseline ID");
            var semanticSha = reader.ReadString(64, "baseline semantic SHA-256");
            var slot = reader.ReadInt32();
            var ticks = reader.ReadInt64();
            DateTimeOffset captured;
            try
            {
                captured = new DateTimeOffset(
                    new DateTime(ticks, DateTimeKind.Utc));
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new InvalidDataException(
                    "Baseline capture timestamp is invalid.",
                    exception);
            }

            var saveData = reader.ReadBytes(
                BaselineBundle.MaximumSaveBytes,
                "save data");
            var hasModded = reader.ReadByte();
            if (hasModded > 1)
            {
                throw new InvalidDataException(
                    "Baseline modded-save presence flag is invalid.");
            }

            var modded = hasModded == 0
                ? null
                : reader.ReadBytes(
                    BaselineBundle.MaximumModdedSaveBytes,
                    "modded save data");
            var snapshot = reader.ReadBytes(
                SemanticSnapshotCanonicalizer.MaximumCanonicalBytes,
                "semantic snapshot");
            RecordingOrigin? origin = null;
            if (schema >= 2)
            {
                var hasOrigin = reader.ReadByte();
                if (hasOrigin > 1)
                    throw new InvalidDataException("Baseline recording-origin presence flag is invalid.");
                if (hasOrigin == 1)
                {
                    try
                    {
                        origin = new RecordingOrigin(
                            reader.ReadString(256, "startup profile ID"),
                            BitConverter.Int64BitsToDouble(reader.ReadInt64()),
                            reader.ReadInt32(),
                            BitConverter.Int64BitsToDouble(reader.ReadInt64()),
                            BitConverter.Int64BitsToDouble(reader.ReadInt64()));
                    }
                    catch (ArgumentException exception)
                    {
                        throw new InvalidDataException("Baseline recording origin is invalid.", exception);
                    }
                }
            }
            reader.RequireEnd();

            return new BaselineBundle(
                schema,
                baselineId,
                semanticSha,
                slot,
                captured,
                saveData,
                modded,
                snapshot,
                origin);
        }
    }
}
