using System;
using System.IO;
using System.Text;

namespace HollowKnightTAS.Core.ReplaySave
{
    internal sealed class ReplaySaveBinaryWriter : IDisposable
    {
        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(false, true);
        private readonly MemoryStream stream = new MemoryStream();

        public void WriteMagic(string value)
        {
            if (value == null || value.Length != 4)
            {
                throw new ArgumentException(
                    "Binary protocol magic must contain four ASCII characters.",
                    nameof(value));
            }

            foreach (var character in value)
            {
                if (character > 0x7f)
                {
                    throw new ArgumentException(
                        "Binary protocol magic must be ASCII.",
                        nameof(value));
                }

                stream.WriteByte((byte)character);
            }
        }

        public void WriteByte(byte value)
        {
            stream.WriteByte(value);
        }

        public void WriteInt16(short value)
        {
            WriteUInt16(unchecked((ushort)value));
        }

        public void WriteUInt16(ushort value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
        }

        public void WriteInt32(int value)
        {
            WriteUInt32(unchecked((uint)value));
        }

        public void WriteUInt32(uint value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
        }

        public void WriteInt64(long value)
        {
            WriteUInt64(unchecked((ulong)value));
        }

        public void WriteUInt64(ulong value)
        {
            for (var shift = 0; shift < 64; shift += 8)
            {
                stream.WriteByte((byte)(value >> shift));
            }
        }

        public void WriteString(string value, int maximumBytes)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var bytes = StrictUtf8.GetBytes(value);
            if (bytes.Length > maximumBytes)
            {
                throw new InvalidDataException(
                    "UTF-8 value exceeds the binary protocol limit.");
            }

            WriteInt32(bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        public void WriteBytes(byte[] value, int maximumBytes)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value.Length > maximumBytes)
            {
                throw new InvalidDataException(
                    "Byte value exceeds the binary protocol limit.");
            }

            WriteInt32(value.Length);
            stream.Write(value, 0, value.Length);
        }

        public byte[] ToArray()
        {
            return stream.ToArray();
        }

        public void Dispose()
        {
            stream.Dispose();
        }
    }

    internal sealed class ReplaySaveBinaryReader
    {
        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(false, true);
        private readonly byte[] bytes;
        private int position;

        public ReplaySaveBinaryReader(byte[] bytes, int maximumBytes)
        {
            this.bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length > maximumBytes)
            {
                throw new InvalidDataException(
                    "Binary protocol object exceeds its size limit.");
            }
        }

        public bool AtEnd => position == bytes.Length;

        public void RequireMagic(string expected)
        {
            if (expected == null || expected.Length != 4)
            {
                throw new ArgumentException(nameof(expected));
            }

            for (var index = 0; index < expected.Length; index++)
            {
                if (ReadByte() != (byte)expected[index])
                {
                    throw new InvalidDataException(
                        "Binary protocol magic is invalid.");
                }
            }
        }

        public byte ReadByte()
        {
            Require(1);
            return bytes[position++];
        }

        public short ReadInt16()
        {
            return unchecked((short)ReadUInt16());
        }

        public ushort ReadUInt16()
        {
            Require(2);
            var result = (ushort)(
                bytes[position]
                | bytes[position + 1] << 8);
            position += 2;
            return result;
        }

        public int ReadInt32()
        {
            return unchecked((int)ReadUInt32());
        }

        public uint ReadUInt32()
        {
            Require(4);
            var result = (uint)(
                bytes[position]
                | bytes[position + 1] << 8
                | bytes[position + 2] << 16
                | bytes[position + 3] << 24);
            position += 4;
            return result;
        }

        public long ReadInt64()
        {
            return unchecked((long)ReadUInt64());
        }

        public ulong ReadUInt64()
        {
            Require(8);
            ulong result = 0;
            for (var shift = 0; shift < 64; shift += 8)
            {
                result |= (ulong)bytes[position++] << shift;
            }

            return result;
        }

        public string ReadString(int maximumBytes, string name)
        {
            var value = StrictUtf8.GetString(
                ReadBytes(maximumBytes, name));
            return value;
        }

        public byte[] ReadBytes(int maximumBytes, string name)
        {
            var count = ReadInt32();
            if (count < 0 || count > maximumBytes)
            {
                throw new InvalidDataException(
                    name + " byte length is outside the allowed range.");
            }

            Require(count);
            var result = new byte[count];
            Buffer.BlockCopy(bytes, position, result, 0, count);
            position += count;
            return result;
        }

        public void RequireEnd()
        {
            if (!AtEnd)
            {
                throw new InvalidDataException(
                    "Binary protocol object has trailing bytes.");
            }
        }

        private void Require(int count)
        {
            if (count < 0 || bytes.Length - position < count)
            {
                throw new InvalidDataException(
                    "Binary protocol object ended before the declared length.");
            }
        }
    }
}
