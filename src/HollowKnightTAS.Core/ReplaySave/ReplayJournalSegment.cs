using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Recording;

namespace HollowKnightTAS.Core.ReplaySave
{
    public sealed class ReplayJournalSegment
    {
        public const int CurrentSchemaVersion = 1;
        public const int MaximumRecords = 4096;
        public const int MaximumObjectBytes = 4 * 1024 * 1024;
        public const string GenesisPreviousSha256 =
            "0000000000000000000000000000000000000000000000000000000000000000";

        private readonly ReadOnlyCollection<JournalRecord> records;

        public ReplayJournalSegment(
            int schemaVersion,
            int sequence,
            string previousObjectSha256,
            IEnumerable<JournalRecord> records)
        {
            if (schemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            if (sequence < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence));
            }

            SchemaVersion = schemaVersion;
            Sequence = sequence;
            PreviousObjectSha256 = ReplaySaveDescriptor.RequireSha256(
                previousObjectSha256,
                nameof(previousObjectSha256));
            if (sequence == 0
                && !string.Equals(
                    PreviousObjectSha256,
                    GenesisPreviousSha256,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The first journal segment must use the genesis previous hash.",
                    nameof(previousObjectSha256));
            }

            if (records == null)
            {
                throw new ArgumentNullException(nameof(records));
            }

            var copy = new List<JournalRecord>(records);
            if (copy.Count == 0 || copy.Count > MaximumRecords)
            {
                throw new ArgumentOutOfRangeException(nameof(records));
            }

            var expectedMovieTick = copy[0].MovieTick;
            if (expectedMovieTick < 0)
            {
                throw new ArgumentException(
                    "Journal movie ticks must be non-negative.",
                    nameof(records));
            }

            foreach (var record in copy)
            {
                if (record.MovieTick != expectedMovieTick)
                {
                    throw new ArgumentException(
                        "Journal segment movie ticks must be contiguous.",
                        nameof(records));
                }

                if (record.Stamp.Phase != TickPhase.InControlCommitted
                    || record.Sample.InputTick != record.Stamp.InputTick)
                {
                    throw new ArgumentException(
                        "Journal records must be committed InControl samples.",
                        nameof(records));
                }

                expectedMovieTick = checked(expectedMovieTick + 1);
            }

            this.records = new ReadOnlyCollection<JournalRecord>(copy);
        }

        public int SchemaVersion { get; }
        public int Sequence { get; }
        public string PreviousObjectSha256 { get; }
        public long FirstMovieTick => records[0].MovieTick;
        public long LastMovieTick => records[records.Count - 1].MovieTick;
        public IReadOnlyList<JournalRecord> Records => records;

        public byte[] Serialize()
        {
            return ReplayJournalSegmentCodec.Serialize(this);
        }

        public string ComputeObjectSha256()
        {
            return Sha256Utility.ComputeHex(Serialize());
        }
    }

    public static class ReplayJournalSegmentCodec
    {
        private const string Magic = "HKTJ";

        public static byte[] Serialize(ReplayJournalSegment value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            using (var writer = new ReplaySaveBinaryWriter())
            {
                writer.WriteMagic(Magic);
                writer.WriteInt32(value.SchemaVersion);
                writer.WriteInt32(value.Sequence);
                writer.WriteString(value.PreviousObjectSha256, 64);
                writer.WriteInt32(value.Records.Count);
                foreach (var record in value.Records)
                {
                    writer.WriteInt64(record.MovieTick);
                    writer.WriteUInt64(record.Stamp.InputTick);
                    writer.WriteInt64(record.Stamp.VisualTick);
                    writer.WriteInt64(record.Stamp.FixedTick);
                    writer.WriteInt32(record.Stamp.SceneEpoch);
                    writer.WriteByte((byte)record.Stamp.Phase);
                    writer.WriteUInt16((ushort)record.Sample.Held);
                    writer.WriteUInt16((ushort)record.Sample.Pressed);
                    writer.WriteUInt16((ushort)record.Sample.Released);
                    writer.WriteInt16(record.Sample.AxisX);
                    writer.WriteInt16(record.Sample.AxisY);
                }

                var bytes = writer.ToArray();
                if (bytes.Length > ReplayJournalSegment.MaximumObjectBytes)
                {
                    throw new InvalidDataException(
                        "Journal segment exceeds its object size limit.");
                }

                return bytes;
            }
        }

        public static ReplayJournalSegment Deserialize(byte[] bytes)
        {
            var reader = new ReplaySaveBinaryReader(
                bytes,
                ReplayJournalSegment.MaximumObjectBytes);
            reader.RequireMagic(Magic);
            var schema = reader.ReadInt32();
            if (schema != ReplayJournalSegment.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    "Unsupported journal segment schema: " + schema + ".");
            }

            var sequence = reader.ReadInt32();
            var previous = reader.ReadString(64, "previous object SHA-256");
            var count = reader.ReadInt32();
            if (count <= 0 || count > ReplayJournalSegment.MaximumRecords)
            {
                throw new InvalidDataException(
                    "Journal segment record count is invalid.");
            }

            var records = new List<JournalRecord>(count);
            for (var index = 0; index < count; index++)
            {
                var movieTick = reader.ReadInt64();
                var inputTick = reader.ReadUInt64();
                var visualTick = reader.ReadInt64();
                var fixedTick = reader.ReadInt64();
                var sceneEpoch = reader.ReadInt32();
                var phaseByte = reader.ReadByte();
                if (!Enum.IsDefined(typeof(TickPhase), phaseByte))
                {
                    throw new InvalidDataException(
                        "Journal segment contains an unknown tick phase.");
                }

                InputSample sample;
                TickStamp stamp;
                try
                {
                    sample = new InputSample(
                        inputTick,
                        (TasAction)reader.ReadUInt16(),
                        (TasAction)reader.ReadUInt16(),
                        (TasAction)reader.ReadUInt16(),
                        reader.ReadInt16(),
                        reader.ReadInt16());
                    stamp = new TickStamp(
                        inputTick,
                        visualTick,
                        fixedTick,
                        sceneEpoch,
                        (TickPhase)phaseByte);
                }
                catch (Exception exception) when (
                    exception is ArgumentException
                    || exception is ArgumentOutOfRangeException)
                {
                    throw new InvalidDataException(
                        "Journal segment contains an invalid record.",
                        exception);
                }

                records.Add(JournalRecord.Create(movieTick, sample, stamp));
            }

            reader.RequireEnd();
            try
            {
                return new ReplayJournalSegment(
                    schema,
                    sequence,
                    previous,
                    records);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    "Journal segment invariants are invalid.",
                    exception);
            }
        }
    }
}
