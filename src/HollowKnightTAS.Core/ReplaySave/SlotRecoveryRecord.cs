using System;
using System.IO;
using System.Text;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Ipc;

namespace HollowKnightTAS.Core.ReplaySave
{
    // Contains no writable path. Consumers derive slot paths from their trusted
    // game-save directory and must prove this exact owner has exited first.
    public sealed class SlotRecoveryRecord
    {
        public const int MaximumFileBytes = ReplayLifecycleLog.MaximumSlotBytes;
        public const int MaximumRecordBytes = MaximumFileBytes * 4 + 1024;
        private readonly byte[]?[] files;

        public SlotRecoveryRecord(string operationId, int processId, DateTimeOffset processStartedUtc,
            int sequence, int slot, byte[]? originalSave, byte[]? originalModded,
            byte[] installedSave, byte[]? installedModded)
        {
            if (!IpcIdentifier.IsValid(operationId, 96)) throw new ArgumentException("Invalid recovery operation.", nameof(operationId));
            if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
            if (processStartedUtc.ToUnixTimeSeconds() <= 0) throw new ArgumentOutOfRangeException(nameof(processStartedUtc));
            if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (slot < 1 || slot > 4) throw new ArgumentOutOfRangeException(nameof(slot));
            if (installedSave == null || installedSave.Length == 0) throw new ArgumentException("Installed save is required.", nameof(installedSave));
            OperationId = operationId;
            ProcessId = processId;
            ProcessStartedUtc = processStartedUtc.ToUniversalTime();
            Sequence = sequence;
            Slot = slot;
            files = new[] { Copy(originalSave), Copy(originalModded), Copy(installedSave), Copy(installedModded) };
        }

        public string OperationId { get; }
        public int ProcessId { get; }
        public DateTimeOffset ProcessStartedUtc { get; }
        public int Sequence { get; }
        public int Slot { get; }
        public byte[]? OriginalSave => Copy(files[0]);
        public byte[]? OriginalModded => Copy(files[1]);
        public byte[] InstalledSave => Copy(files[2])!;
        public byte[]? InstalledModded => Copy(files[3]);
        public bool MatchesOwner(string operationId, int processId, DateTimeOffset startedUtc)
            => OperationId == operationId && ProcessId == processId && ProcessStartedUtc == startedUtc;
        public bool CanRestore(byte[]? currentSave, byte[]? currentModded)
            => SlotRollbackGuard.CanRestore(currentSave, files[0], files[2])
                && SlotRollbackGuard.CanRestore(currentModded, files[1], files[3]);

        public byte[] Serialize()
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true))
                {
                    writer.Write("HKSR1");
                    writer.Write(OperationId);
                    writer.Write(ProcessId);
                    writer.Write(ProcessStartedUtc.UtcDateTime.Ticks);
                    writer.Write(Sequence);
                    writer.Write(Slot);
                    foreach (var bytes in files)
                    {
                        writer.Write(bytes?.Length ?? -1);
                        if (bytes != null) writer.Write(bytes);
                    }
                }
                return stream.ToArray();
            }
        }

        public static SlotRecoveryRecord Deserialize(byte[] bytes, string expectedSha256)
        {
            ReplaySaveDescriptor.RequireSha256(expectedSha256, nameof(expectedSha256));
            if (bytes == null || bytes.Length > MaximumRecordBytes
                || Sha256Utility.ComputeHex(bytes) != expectedSha256)
                throw new InvalidDataException("Recovery record hash or size mismatch.");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, new UTF8Encoding(false, true)))
            {
                if (reader.ReadString() != "HKSR1") throw new InvalidDataException("Unknown recovery record format.");
                var operation = reader.ReadString();
                var pid = reader.ReadInt32();
                var started = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
                var sequence = reader.ReadInt32();
                var slot = reader.ReadInt32();
                var originalSave = ReadFile(reader);
                var originalModded = ReadFile(reader);
                var installedSave = ReadFile(reader);
                var installedModded = ReadFile(reader);
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing recovery data.");
                var result = new SlotRecoveryRecord(operation, pid, started, sequence, slot,
                    originalSave, originalModded, installedSave!, installedModded);
                if (Sha256Utility.ComputeHex(result.Serialize()) != expectedSha256)
                    throw new InvalidDataException("Noncanonical recovery record.");
                return result;
            }
        }

        private static byte[]? Copy(byte[]? bytes)
        {
            if (bytes != null && bytes.Length > MaximumFileBytes) throw new ArgumentException("Slot file exceeds recovery limit.");
            return bytes == null ? null : (byte[])bytes.Clone();
        }
        private static byte[]? ReadFile(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            if (length == -1) return null;
            if (length < 0 || length > MaximumFileBytes || length > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Invalid recovery file length.");
            return reader.ReadBytes(length);
        }
    }
}
