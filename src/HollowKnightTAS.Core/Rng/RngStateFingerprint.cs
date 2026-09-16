using System;
using HollowKnightTAS.Core.Cryptography;

namespace HollowKnightTAS.Core.Rng
{
    public readonly struct RngStateFingerprint :
        IEquatable<RngStateFingerprint>
    {
        public RngStateFingerprint(string codecId, string sha256)
        {
            CodecId = RngValidation.RequireIdentifier(
                codecId,
                nameof(codecId));
            Sha256 = RngValidation.RequireSha256(
                sha256,
                nameof(sha256));
        }

        public string CodecId { get; }
        public string Sha256 { get; }

        public static RngStateFingerprint FromBytes(
            string codecId,
            byte[] canonicalBytes)
        {
            if (canonicalBytes == null)
            {
                throw new ArgumentNullException(nameof(canonicalBytes));
            }

            return new RngStateFingerprint(
                codecId,
                Sha256Utility.ComputeHex(canonicalBytes));
        }

        public bool Equals(RngStateFingerprint other)
        {
            return string.Equals(
                       CodecId,
                       other.CodecId,
                       StringComparison.Ordinal)
                   && string.Equals(
                       Sha256,
                       other.Sha256,
                       StringComparison.Ordinal);
        }

        public override bool Equals(object? value)
        {
            return value is RngStateFingerprint other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((CodecId == null
                            ? 0
                            : StringComparer.Ordinal.GetHashCode(CodecId))
                        * 397)
                       ^ (Sha256 == null
                           ? 0
                           : StringComparer.Ordinal.GetHashCode(Sha256));
            }
        }
    }

    internal static class RngValidation
    {
        public static string RequireIdentifier(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.Length > 128)
            {
                throw new ArgumentException(
                    "A non-empty identifier of at most 128 characters is required.",
                    name);
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!(character >= 'a' && character <= 'z')
                    && !(character >= 'A' && character <= 'Z')
                    && !(character >= '0' && character <= '9')
                    && character != '.'
                    && character != '-'
                    && character != '_'
                    && character != ':')
                {
                    throw new ArgumentException(
                        "Identifier contains an unsupported character.",
                        name);
                }
            }

            return value;
        }

        public static string RequireText(
            string value,
            int maximumLength,
            string name)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.Length > maximumLength)
            {
                throw new ArgumentException(
                    "A bounded non-empty value is required.",
                    name);
            }

            return value;
        }

        public static string RequireSha256(string value, string name)
        {
            if (value == null || value.Length != 64)
            {
                throw new ArgumentException(
                    "A lowercase SHA-256 value is required.",
                    name);
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!(character >= '0' && character <= '9')
                    && !(character >= 'a' && character <= 'f'))
                {
                    throw new ArgumentException(
                        "A lowercase SHA-256 value is required.",
                        name);
                }
            }

            return value;
        }
    }
}
