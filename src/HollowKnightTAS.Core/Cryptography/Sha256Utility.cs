using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HollowKnightTAS.Core.Cryptography
{
    public static class Sha256Utility
    {
        public static string ComputeHex(byte[] value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            using (var algorithm = SHA256.Create())
            {
                return ToLowerHex(algorithm.ComputeHash(value));
            }
        }

        public static string ComputeUtf8Hex(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return ComputeHex(new UTF8Encoding(false, true).GetBytes(value));
        }

        public static string ComputeFileHex(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A file path is required.", nameof(path));
            }

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var algorithm = SHA256.Create())
            {
                return ToLowerHex(algorithm.ComputeHash(stream));
            }
        }

        private static string ToLowerHex(byte[] value)
        {
            var builder = new StringBuilder(value.Length * 2);
            foreach (var item in value)
            {
                builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}

