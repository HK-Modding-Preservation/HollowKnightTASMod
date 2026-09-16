using System;
using System.Globalization;
using System.Text;
using HollowKnightTAS.Core.Ledger;

namespace HollowKnightTAS.Core.State
{
    public readonly struct SemanticValue : IEquatable<SemanticValue>
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly byte[]? canonicalBytes;

        private SemanticValue(
            SemanticValueKind kind,
            byte[] bytes,
            string displayValue)
        {
            Kind = kind;
            canonicalBytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
            DisplayValue = displayValue
                           ?? throw new ArgumentNullException(nameof(displayValue));
        }

        public SemanticValueKind Kind { get; }
        public byte[] CanonicalBytes => GetCanonicalBytes();
        public string DisplayValue { get; }

        public static SemanticValue FromBoolean(bool value)
        {
            return new SemanticValue(
                SemanticValueKind.Boolean,
                new[] { value ? (byte)1 : (byte)0 },
                value ? "true" : "false");
        }

        public static SemanticValue FromInt32(int value)
        {
            return new SemanticValue(
                SemanticValueKind.Int32,
                EncodeInt32(value),
                value.ToString(CultureInfo.InvariantCulture));
        }

        public static SemanticValue FromInt64(long value)
        {
            return new SemanticValue(
                SemanticValueKind.Int64,
                EncodeInt64(value),
                value.ToString(CultureInfo.InvariantCulture));
        }

        public static SemanticValue FromFloat32(float value)
        {
            return FromFloat32Bits(SingleBits.FromSingle(value));
        }

        public static SemanticValue FromFloat32Bits(int bits)
        {
            var value = SingleBits.ToSingle(bits);
            return new SemanticValue(
                SemanticValueKind.Float32Bits,
                EncodeInt32(bits),
                value.ToString("R", CultureInfo.InvariantCulture));
        }

        public static SemanticValue FromString(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return new SemanticValue(
                SemanticValueKind.Utf8String,
                StrictUtf8.GetBytes(value),
                value);
        }

        public string CanonicalHex
        {
            get
            {
                var bytes = GetCanonicalBytes();
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var value in bytes)
                {
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        public byte[] GetCanonicalBytes()
        {
            if (canonicalBytes == null)
            {
                throw new InvalidOperationException(
                    "The default SemanticValue is not valid.");
            }

            return (byte[])canonicalBytes.Clone();
        }

        public bool Equals(SemanticValue other)
        {
            if (canonicalBytes == null || other.canonicalBytes == null)
            {
                return canonicalBytes == null && other.canonicalBytes == null;
            }

            if (Kind != other.Kind
                || canonicalBytes.Length != other.canonicalBytes.Length)
            {
                return false;
            }

            for (var index = 0; index < canonicalBytes.Length; index++)
            {
                if (canonicalBytes[index] != other.canonicalBytes[index])
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? value)
        {
            return value is SemanticValue other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Kind;
                if (canonicalBytes != null)
                {
                    foreach (var value in canonicalBytes)
                    {
                        hash = (hash * 397) ^ value;
                    }
                }

                return hash;
            }
        }

        internal static SemanticValue FromCanonicalBytes(
            SemanticValueKind kind,
            byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            switch (kind)
            {
                case SemanticValueKind.Boolean:
                    if (bytes.Length != 1 || bytes[0] > 1)
                    {
                        throw new ArgumentException(
                            "Boolean canonical bytes must be exactly 00 or 01.",
                            nameof(bytes));
                    }

                    return FromBoolean(bytes[0] == 1);
                case SemanticValueKind.Int32:
                    RequireLength(bytes, 4, kind);
                    return FromInt32(DecodeInt32(bytes));
                case SemanticValueKind.Int64:
                    RequireLength(bytes, 8, kind);
                    return FromInt64(DecodeInt64(bytes));
                case SemanticValueKind.Float32Bits:
                    RequireLength(bytes, 4, kind);
                    return FromFloat32Bits(DecodeInt32(bytes));
                case SemanticValueKind.Utf8String:
                    return FromString(StrictUtf8.GetString(bytes));
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown value kind.");
            }
        }

        internal static byte[] EncodeInt32(int value)
        {
            unchecked
            {
                return new[]
                {
                    (byte)((uint)value >> 24),
                    (byte)((uint)value >> 16),
                    (byte)((uint)value >> 8),
                    (byte)value
                };
            }
        }

        internal static byte[] EncodeInt64(long value)
        {
            unchecked
            {
                var unsigned = (ulong)value;
                return new[]
                {
                    (byte)(unsigned >> 56),
                    (byte)(unsigned >> 48),
                    (byte)(unsigned >> 40),
                    (byte)(unsigned >> 32),
                    (byte)(unsigned >> 24),
                    (byte)(unsigned >> 16),
                    (byte)(unsigned >> 8),
                    (byte)unsigned
                };
            }
        }

        internal static int DecodeInt32(byte[] bytes)
        {
            unchecked
            {
                return (int)(((uint)bytes[0] << 24)
                             | ((uint)bytes[1] << 16)
                             | ((uint)bytes[2] << 8)
                             | bytes[3]);
            }
        }

        internal static long DecodeInt64(byte[] bytes)
        {
            unchecked
            {
                return (long)(((ulong)bytes[0] << 56)
                              | ((ulong)bytes[1] << 48)
                              | ((ulong)bytes[2] << 40)
                              | ((ulong)bytes[3] << 32)
                              | ((ulong)bytes[4] << 24)
                              | ((ulong)bytes[5] << 16)
                              | ((ulong)bytes[6] << 8)
                              | bytes[7]);
            }
        }

        private static void RequireLength(
            byte[] bytes,
            int expected,
            SemanticValueKind kind)
        {
            if (bytes.Length != expected)
            {
                throw new ArgumentException(
                    kind + " canonical bytes must have length " + expected + ".",
                    nameof(bytes));
            }
        }
    }
}
