using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Ipc;

namespace HollowKnightTAS.Core.ReplaySave
{
    public sealed class SlotRecoveryOwner
    {
        internal SlotRecoveryOwner(SlotRecoveryRecord record)
        { OperationId = record.OperationId; ProcessId = record.ProcessId; ProcessStartedUtc = record.ProcessStartedUtc; }
        public string OperationId { get; }
        public int ProcessId { get; }
        public DateTimeOffset ProcessStartedUtc { get; }
    }

    public sealed class SlotRecoveryStore
    {
        private readonly string root;
        public SlotRecoveryStore(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Explicit recovery root required.", nameof(root));
            this.root = Path.GetFullPath(root);
        }

        // Call before the first slot write. A successful return means record
        // bytes were flushed and atomically published in the operation folder.
        public string Publish(SlotRecoveryRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var bytes = record.Serialize();
            var hash = Sha256Utility.ComputeHex(bytes);
            var directory = OperationDirectory(record.OperationId);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, hash + ".pending");
            if (File.Exists(path)) { Load(record.OperationId, hash); return hash; }
            var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }
                File.Move(temporary, path);
                return hash;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public SlotRecoveryRecord Load(string operationId, string hash)
        {
            ReplaySaveDescriptor.RequireSha256(hash, nameof(hash));
            var path = Path.Combine(OperationDirectory(operationId), hash + ".pending");
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (file.Length > SlotRecoveryRecord.MaximumRecordBytes) throw new InvalidDataException("Recovery record too large.");
                using (var reader = new BinaryReader(file))
                {
                    var record = SlotRecoveryRecord.Deserialize(reader.ReadBytes(checked((int)file.Length)), hash);
                    if (record.OperationId != operationId) throw new InvalidDataException("Recovery operation mismatch.");
                    return record;
                }
            }
        }

        public IReadOnlyList<SlotRecoveryOwner> FindPendingOwners()
        {
            if (!Directory.Exists(root)) return Array.Empty<SlotRecoveryOwner>();
            RejectReparse(root);
            var directories = Directory.EnumerateDirectories(root).Take(4097).ToArray();
            if (directories.Length > 4096) throw new InvalidDataException("Recovery directory limit exceeded.");
            var owners = new List<SlotRecoveryOwner>();
            foreach (var directory in directories)
            {
                RejectReparse(directory);
                var path = Directory.EnumerateFiles(directory, "*.pending").FirstOrDefault();
                if (path == null) continue;
                RejectReparse(path);
                var hash = Path.GetFileNameWithoutExtension(path);
                ReplaySaveDescriptor.RequireSha256(hash, nameof(hash));
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(file))
                {
                    if (file.Length > SlotRecoveryRecord.MaximumRecordBytes) throw new InvalidDataException("Recovery record too large.");
                    var record = SlotRecoveryRecord.Deserialize(reader.ReadBytes(checked((int)file.Length)), hash);
                    if (!string.Equals(OperationDirectory(record.OperationId), directory, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Recovery directory identity mismatch.");
                    owners.Add(new SlotRecoveryOwner(record));
                }
            }
            return owners.OrderByDescending(x => x.ProcessStartedUtc).ToArray();
        }

        public IReadOnlyList<KeyValuePair<string, SlotRecoveryRecord>> ReadPending(string operationId)
        {
            var directory = OperationDirectory(operationId);
            if (!Directory.Exists(directory)) return Array.Empty<KeyValuePair<string, SlotRecoveryRecord>>();
            RejectReparse(root);
            RejectReparse(directory);
            var paths = Directory.EnumerateFiles(directory, "*.pending", SearchOption.TopDirectoryOnly).Take(4097).ToArray();
            if (paths.Length > 4096) throw new InvalidDataException("Too many pending slot records.");
            var records = paths.Select(path =>
            {
                RejectReparse(path);
                var hash = Path.GetFileNameWithoutExtension(path);
                return new KeyValuePair<string, SlotRecoveryRecord>(hash, Load(operationId, hash));
            }).OrderByDescending(x => x.Value.Sequence).ToArray();
            if (records.Select(x => x.Value.Sequence).Distinct().Count() != records.Length)
                throw new InvalidDataException("Ambiguous recovery sequence.");
            return records;
        }

        public int RecoverAfterExit(string operationId, int processId, DateTimeOffset processStartedUtc,
            string saveRoot, Func<bool> mayWrite)
        {
            if (mayWrite == null || !mayWrite()) throw new InvalidOperationException("Game exit is not confirmed; no slots restored.");
            var directory = Path.GetFullPath(saveRoot);
            RejectReparse(directory);
            var records = ReadPending(operationId);
            if (records.Any(x => !x.Value.MatchesOwner(operationId, processId, processStartedUtc)))
                throw new InvalidDataException("Pending recovery belongs to another target process.");
            var count = 0;
            foreach (var item in records)
            {
                if (!mayWrite()) throw new InvalidOperationException("A game process may be using slots; recovery stopped.");
                var record = item.Value;
                var save = Path.Combine(directory, "user" + record.Slot + ".dat");
                var modded = Path.Combine(directory, "user" + record.Slot + ".modded.json");
                var currentSave = ReadSlot(save);
                var currentModded = ReadSlot(modded);
                if (!record.CanRestore(currentSave, currentModded))
                    throw new InvalidOperationException("Slot " + record.Slot + " changed; current files and pending recovery were preserved.");
                RestoreFile(save, currentSave, record.OriginalSave, mayWrite);
                RestoreFile(modded, currentModded, record.OriginalModded, mayWrite);
                MarkComplete(operationId, item.Key, false);
                count++;
            }
            return count;
        }

        private static byte[]? ReadSlot(string path)
        {
            if (!File.Exists(path)) return null;
            RejectReparse(path);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > SlotRecoveryRecord.MaximumFileBytes) throw new InvalidDataException("Slot exceeds recovery size limit.");
                using (var reader = new BinaryReader(stream)) return reader.ReadBytes(checked((int)stream.Length));
            }
        }

        private static void RestoreFile(string path, byte[]? expected, byte[]? original, Func<bool> mayWrite)
        {
            if (!mayWrite() || !SlotRollbackGuard.CanRestore(ReadSlot(path), expected, expected))
                throw new InvalidOperationException("Slot changed during recovery; pending record retained.");
            if (SlotRollbackGuard.CanRestore(expected, original, original)) return;
            if (original == null) { File.Delete(path); return; }
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(original, 0, original.Length);
                    file.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void RejectReparse(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Recovery refuses redirected paths.");
        }

        public void MarkComplete(string operationId, string hash, bool committed)
        {
            ReplaySaveDescriptor.RequireSha256(hash, nameof(hash));
            var directory = OperationDirectory(operationId);
            var pending = Path.Combine(directory, hash + ".pending");
            var complete = Path.Combine(directory, hash + (committed ? ".committed" : ".restored"));
            if (!File.Exists(pending) && File.Exists(complete)) return;
            Load(operationId, hash);
            File.Move(pending, complete);
        }

        private string OperationDirectory(string operationId)
        {
            if (!IpcIdentifier.IsValid(operationId, 96)) throw new ArgumentException("Invalid recovery operation.", nameof(operationId));
            // IPC identifiers permit dots and colons; never use them directly
            // as filesystem components (parent paths or Windows ADS names).
            return Path.Combine(root, Sha256Utility.ComputeHex(Encoding.UTF8.GetBytes(operationId)));
        }
    }
}
