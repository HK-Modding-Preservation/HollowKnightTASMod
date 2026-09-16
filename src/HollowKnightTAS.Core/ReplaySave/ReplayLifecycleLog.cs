using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Core.ReplaySave
{
    public enum ReplayLifecycleKind : byte { ReturnToMenu = 1, LoadSlot = 2 }
    public enum ReplayLifecycleOutcome : byte { Waiting = 1, Completed = 2, Failed = 3 }

    // An operation occurs after AfterMovieTick and before the next input sample.
    // Sequence orders operations sharing that boundary. NativeFrameCount records
    // elapsed engine frames, not wall time and not additional input movie ticks.
    public sealed class ReplayLifecycleRecord
    {
        public ReplayLifecycleRecord(int sequence, long afterMovieTick,
            string inputPrefixSha256, ReplayLifecycleKind kind, int slot,
            string slotObjectSha256, ReplayLifecycleOutcome outcome,
            long nativeFrameCount, string failure, string? moddedSlotObjectSha256 = null)
        {
            if (sequence < 0 || afterMovieTick < -1 || nativeFrameCount < 0)
                throw new ArgumentOutOfRangeException(nameof(sequence));
            if (kind != ReplayLifecycleKind.ReturnToMenu && kind != ReplayLifecycleKind.LoadSlot)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (outcome != ReplayLifecycleOutcome.Waiting && outcome != ReplayLifecycleOutcome.Completed
                && outcome != ReplayLifecycleOutcome.Failed)
                throw new ArgumentOutOfRangeException(nameof(outcome));
            if (failure == null || System.Text.Encoding.UTF8.GetByteCount(failure) > 512
                || (outcome == ReplayLifecycleOutcome.Failed ? string.IsNullOrWhiteSpace(failure) : failure.Length != 0))
                throw new ArgumentException("Only failed operations require a bounded failure reason.", nameof(failure));
            if (kind == ReplayLifecycleKind.LoadSlot)
            {
                if (slot < 1 || slot > 4) throw new ArgumentOutOfRangeException(nameof(slot));
                SlotObjectSha256 = ReplaySaveDescriptor.RequireSha256(slotObjectSha256, nameof(slotObjectSha256));
                ModdedSlotObjectSha256 = string.IsNullOrEmpty(moddedSlotObjectSha256)
                    ? moddedSlotObjectSha256
                    : ReplaySaveDescriptor.RequireSha256(moddedSlotObjectSha256!, nameof(moddedSlotObjectSha256));
            }
            else
            {
                if (slot != 0 || slotObjectSha256 != string.Empty || moddedSlotObjectSha256 != null)
                    throw new ArgumentException("Return-to-menu has no slot object.");
                SlotObjectSha256 = string.Empty;
            }
            Sequence = sequence;
            AfterMovieTick = afterMovieTick;
            InputPrefixSha256 = ReplaySaveDescriptor.RequireSha256(inputPrefixSha256, nameof(inputPrefixSha256));
            Kind = kind;
            Slot = slot;
            Outcome = outcome;
            NativeFrameCount = nativeFrameCount;
            Failure = failure;
        }

        public int Sequence { get; }
        public long AfterMovieTick { get; }
        public string InputPrefixSha256 { get; }
        public ReplayLifecycleKind Kind { get; }
        public int Slot { get; }
        public string SlotObjectSha256 { get; }
        // null: legacy capture unknown; empty: file absent; SHA: exact bytes.
        public string? ModdedSlotObjectSha256 { get; }
        public ReplayLifecycleOutcome Outcome { get; }
        public long NativeFrameCount { get; }
        public string Failure { get; }
    }

    // Separate from the v1 frame journal: old readers must not silently skip
    // native lifecycle operations. Descriptor/runtime wiring is required before
    // this format can be used as a restorable recording.
    public sealed class ReplayLifecycleLog
    {
        public const int SchemaVersion = 2;
        public const int MaximumRecords = 4096;
        public const int MaximumBytes = 4 * 1024 * 1024;
        public const int MaximumSlotBytes = 16 * 1024 * 1024;

        public ReplayLifecycleLog(string rootBaselineSha256, IEnumerable<ReplayLifecycleRecord> records)
        {
            RootBaselineSha256 = ReplaySaveDescriptor.RequireSha256(rootBaselineSha256, nameof(rootBaselineSha256));
            if (records == null) throw new ArgumentNullException(nameof(records));
            var copy = new List<ReplayLifecycleRecord>();
            long previousTick = -1;
            bool terminal = false;
            string? previousPrefix = null;
            foreach (var record in records)
            {
                if (record == null || copy.Count >= MaximumRecords || terminal
                    || record.Sequence != copy.Count || record.AfterMovieTick < previousTick)
                    throw new ArgumentException("Lifecycle records must be bounded, ordered and uniquely sequenced.", nameof(records));
                if (previousPrefix != null && record.AfterMovieTick == previousTick
                    && record.InputPrefixSha256 != previousPrefix)
                    throw new ArgumentException("Operations at the same input boundary must bind the same prefix.", nameof(records));
                copy.Add(record);
                previousTick = record.AfterMovieTick;
                previousPrefix = record.InputPrefixSha256;
                terminal = record.Outcome != ReplayLifecycleOutcome.Completed;
            }
            Records = new ReadOnlyCollection<ReplayLifecycleRecord>(copy);
        }

        public string RootBaselineSha256 { get; }
        public IReadOnlyList<ReplayLifecycleRecord> Records { get; }
        public bool IsCompleted => Records.Count == 0
            || Records[Records.Count - 1].Outcome == ReplayLifecycleOutcome.Completed;

        public byte[] Serialize()
        {
            using (var writer = new ReplaySaveBinaryWriter())
            {
                writer.WriteMagic("HKLC");
                writer.WriteInt32(SchemaVersion);
                writer.WriteString(RootBaselineSha256, 64);
                writer.WriteInt32(Records.Count);
                foreach (var r in Records)
                {
                    writer.WriteInt32(r.Sequence);
                    writer.WriteInt64(r.AfterMovieTick);
                    writer.WriteString(r.InputPrefixSha256, 64);
                    writer.WriteByte((byte)r.Kind);
                    writer.WriteInt32(r.Slot);
                    writer.WriteString(r.SlotObjectSha256, 64);
                    writer.WriteByte((byte)r.Outcome);
                    writer.WriteInt64(r.NativeFrameCount);
                    writer.WriteString(r.Failure, 512);
                    writer.WriteByte(r.ModdedSlotObjectSha256 == null ? (byte)0 : (byte)1);
                    if (r.ModdedSlotObjectSha256 != null) writer.WriteString(r.ModdedSlotObjectSha256, 64);
                }
                var bytes = writer.ToArray();
                if (bytes.Length > MaximumBytes) throw new InvalidDataException("Lifecycle log is too large.");
                return bytes;
            }
        }

        public static ReplayLifecycleLog Deserialize(byte[] bytes)
        {
            var reader = new ReplaySaveBinaryReader(bytes, MaximumBytes);
            reader.RequireMagic("HKLC");
            var schema = reader.ReadInt32();
            if (schema != 1 && schema != SchemaVersion) throw new InvalidDataException("Unsupported lifecycle schema.");
            var root = reader.ReadString(64, "root baseline");
            var count = reader.ReadInt32();
            if (count < 0 || count > MaximumRecords) throw new InvalidDataException("Invalid lifecycle count.");
            var records = new List<ReplayLifecycleRecord>();
            for (var i = 0; i < count; i++)
            {
                var record = new ReplayLifecycleRecord(reader.ReadInt32(), reader.ReadInt64(),
                    reader.ReadString(64, "input prefix"), (ReplayLifecycleKind)reader.ReadByte(),
                    reader.ReadInt32(), reader.ReadString(64, "slot object"),
                    (ReplayLifecycleOutcome)reader.ReadByte(), reader.ReadInt64(),
                    reader.ReadString(512, "failure"));
                string? modded = null;
                if (schema >= 2)
                {
                    var known = reader.ReadByte();
                    if (known > 1) throw new InvalidDataException("Invalid modded slot presence flag.");
                    if (known == 1) modded = reader.ReadString(64, "modded slot object");
                }
                records.Add(new ReplayLifecycleRecord(record.Sequence, record.AfterMovieTick,
                    record.InputPrefixSha256, record.Kind, record.Slot, record.SlotObjectSha256,
                    record.Outcome, record.NativeFrameCount, record.Failure, modded));
            }
            reader.RequireEnd();
            return new ReplayLifecycleLog(root, records);
        }

        public void VerifyInputPrefixes(MovieDocument movie)
        {
            if (movie == null) throw new ArgumentNullException(nameof(movie));
            // Several operations can occupy one boundary; compute that prefix
            // once. Tick -1 denotes the empty prefix, tick 0 includes sample 0.
            var previousBoundary = long.MinValue;
            string? prefix = null;
            foreach (var record in Records)
            {
                if (record.AfterMovieTick != previousBoundary)
                {
                    try { prefix = MoviePrefixIdentity.ComputeSha256(movie, checked(record.AfterMovieTick + 1)); }
                    catch (Exception exception) when (exception is ArgumentException || exception is OverflowException)
                    { throw new InvalidDataException("Lifecycle operation lies outside the input movie.", exception); }
                    previousBoundary = record.AfterMovieTick;
                }
                if (prefix != record.InputPrefixSha256)
                    throw new InvalidDataException("Lifecycle input prefix mismatch at operation " + record.Sequence + ".");
            }
        }

        public void VerifySlotObjects(Func<string, byte[]?> readObject)
        {
            if (readObject == null) throw new ArgumentNullException(nameof(readObject));
            var verified = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
            foreach (var r in Records)
            {
                if (r.Kind != ReplayLifecycleKind.LoadSlot) continue;
                Verify(r.SlotObjectSha256, false);
                if (!string.IsNullOrEmpty(r.ModdedSlotObjectSha256)) Verify(r.ModdedSlotObjectSha256!, true);
            }
            void Verify(string hash, bool allowEmpty)
            {
                if (!verified.TryGetValue(hash, out var bytes))
                {
                    bytes = readObject(hash);
                    verified.Add(hash, bytes);
                }
                if (bytes == null || (!allowEmpty && bytes.Length == 0) || bytes.Length > MaximumSlotBytes
                    || Sha256Utility.ComputeHex(bytes) != hash)
                    throw new InvalidDataException("Missing or invalid lifecycle slot object: " + hash);
            }
        }
    }
}
