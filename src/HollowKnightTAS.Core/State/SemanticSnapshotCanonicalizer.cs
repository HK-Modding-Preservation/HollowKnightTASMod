using System;
using System.IO;
using System.Text;

namespace HollowKnightTAS.Core.State
{
    public static class SemanticSnapshotCanonicalizer
    {
        public const int MaximumCanonicalBytes = 1024 * 1024;
        private static readonly byte[] Magic = { 0x48, 0x4b, 0x53, 0x53 };
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static byte[] Serialize(SemanticSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            using (var stream = new MemoryStream())
            {
                stream.Write(Magic, 0, Magic.Length);
                WriteInt32(stream, snapshot.SchemaVersion);
                WriteInt32(stream, snapshot.Values.Count);
                foreach (var entry in snapshot.Values)
                {
                    var keyBytes = StrictUtf8.GetBytes(entry.Key);
                    var valueBytes = entry.Value.GetCanonicalBytes();
                    WriteInt32(stream, keyBytes.Length);
                    stream.Write(keyBytes, 0, keyBytes.Length);
                    stream.WriteByte((byte)entry.Value.Kind);
                    WriteInt32(stream, valueBytes.Length);
                    stream.Write(valueBytes, 0, valueBytes.Length);
                }

                if (stream.Length > MaximumCanonicalBytes)
                {
                    throw new InvalidOperationException(
                        "Semantic snapshot canonical representation exceeds the size limit.");
                }

                return stream.ToArray();
            }
        }

        public static SemanticSnapshot Deserialize(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            if (bytes.Length > MaximumCanonicalBytes)
            {
                throw new InvalidDataException(
                    "Semantic snapshot canonical representation exceeds the size limit.");
            }

            var cursor = new Cursor(bytes);
            foreach (var expected in Magic)
            {
                if (cursor.ReadByte() != expected)
                {
                    throw new InvalidDataException(
                        "Semantic snapshot magic is invalid.");
                }
            }

            var schemaVersion = cursor.ReadInt32();
            if (!SemanticSnapshotSchemas.IsSupported(schemaVersion))
            {
                throw new InvalidDataException(
                    "Unsupported semantic snapshot schema: " + schemaVersion + ".");
            }

            var count = cursor.ReadInt32();
            if (count != SemanticSnapshotSchemas.Keys(schemaVersion).Count)
            {
                throw new InvalidDataException(
                    "Semantic snapshot schema " + schemaVersion + " must contain exactly "
                    + SemanticSnapshotSchemas.Keys(schemaVersion).Count
                    + " entries.");
            }

            var builder = new SemanticSnapshotBuilder(schemaVersion);
            string? previousKey = null;
            for (var index = 0; index < count; index++)
            {
                var keyLength = cursor.ReadBoundedLength(1, 256, "key");
                var key = StrictUtf8.GetString(cursor.ReadBytes(keyLength));
                if (previousKey != null
                    && StringComparer.Ordinal.Compare(previousKey, key) >= 0)
                {
                    throw new InvalidDataException(
                        "Semantic snapshot keys are duplicate or not in canonical order.");
                }

                previousKey = key;
                var kindByte = cursor.ReadByte();
                if (!Enum.IsDefined(typeof(SemanticValueKind), kindByte))
                {
                    throw new InvalidDataException(
                        "Unknown semantic value kind: " + kindByte + ".");
                }

                var kind = (SemanticValueKind)kindByte;
                var valueLength = cursor.ReadBoundedLength(
                    0,
                    MaximumCanonicalBytes,
                    "value");
                var valueBytes = cursor.ReadBytes(valueLength);
                try
                {
                    builder.Add(
                        key,
                        SemanticValue.FromCanonicalBytes(kind, valueBytes));
                }
                catch (Exception exception) when (
                    exception is ArgumentException
                    || exception is InvalidOperationException
                    || exception is DecoderFallbackException)
                {
                    throw new InvalidDataException(
                        "Invalid semantic snapshot entry " + key + ": " + exception.Message,
                        exception);
                }
            }

            if (!cursor.AtEnd)
            {
                throw new InvalidDataException(
                    "Semantic snapshot has trailing bytes.");
            }

            try
            {
                return builder.Build();
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidDataException(exception.Message, exception);
            }
        }

        private static void WriteInt32(Stream stream, int value)
        {
            var bytes = SemanticValue.EncodeInt32(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private sealed class Cursor
        {
            private readonly byte[] bytes;
            private int position;

            public Cursor(byte[] bytes)
            {
                this.bytes = bytes;
            }

            public bool AtEnd => position == bytes.Length;

            public byte ReadByte()
            {
                EnsureAvailable(1);
                return bytes[position++];
            }

            public int ReadInt32()
            {
                return SemanticValue.DecodeInt32(ReadBytes(4));
            }

            public int ReadBoundedLength(int minimum, int maximum, string name)
            {
                var value = ReadInt32();
                if (value < minimum || value > maximum)
                {
                    throw new InvalidDataException(
                        "Semantic snapshot "
                        + name
                        + " length is outside ["
                        + minimum
                        + ", "
                        + maximum
                        + "].");
                }

                return value;
            }

            public byte[] ReadBytes(int count)
            {
                EnsureAvailable(count);
                var result = new byte[count];
                Buffer.BlockCopy(bytes, position, result, 0, count);
                position += count;
                return result;
            }

            private void EnsureAvailable(int count)
            {
                if (count < 0 || bytes.Length - position < count)
                {
                    throw new InvalidDataException(
                        "Semantic snapshot ended before the declared data length.");
                }
            }
        }
    }
}
