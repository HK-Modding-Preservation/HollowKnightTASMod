using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HollowKnightTAS.Core.Cryptography;

namespace HollowKnightTAS.Core.Verification
{
    public static class RunSignature
    {
        private const int FormatVersion = 1;
        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(false, true);

        public static string Compute(RunEvidence evidence)
        {
            if (evidence == null)
            {
                throw new ArgumentNullException(nameof(evidence));
            }

            using (var stream = new MemoryStream())
            {
                WriteInt32(stream, FormatVersion);
                WriteString(stream, evidence.ManifestSha256);
                WriteString(stream, evidence.BaselineSha256);
                WriteString(stream, evidence.MovieId);
                WriteInt32(stream, evidence.SnapshotSchemaVersion);
                WriteInt32(stream, evidence.LedgerSchemaVersion);
                WriteInt32(stream, evidence.Milestones.Count);
                foreach (var milestone in evidence.Milestones)
                {
                    WriteString(stream, milestone.MilestoneId);
                    WriteInt64(stream, milestone.MovieTick);
                    WriteInt32(stream, milestone.TickStamp.SceneEpoch);
                    stream.WriteByte((byte)milestone.TickStamp.Phase);
                    WriteString(stream, milestone.SceneName);
                    WriteString(stream, milestone.SemanticSha256);
                    WriteString(stream, milestone.LedgerWindowSha256);
                    WriteString(stream, milestone.RngStateSha256);
                }

                return Sha256Utility.ComputeHex(stream.ToArray());
            }
        }

        public static string ComputeLedgerWindowSha256(
            IEnumerable<VerificationLedgerEntry> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            using (var stream = new MemoryStream())
            {
                WriteInt32(stream, FormatVersion);
                var countPosition = stream.Position;
                WriteInt32(stream, 0);
                var count = 0;
                foreach (var entry in entries)
                {
                    if (entry == null)
                    {
                        throw new ArgumentException(
                            "Ledger window cannot contain null.",
                            nameof(entries));
                    }

                    count++;
                    WriteInt64(stream, entry.MovieTick);
                    WriteInt32(stream, entry.Stamp.SceneEpoch);
                    stream.WriteByte((byte)entry.Stamp.Phase);
                    WriteInt32(stream, (int)entry.Input.Held);
                    WriteInt32(stream, (int)entry.Input.Pressed);
                    WriteInt32(stream, (int)entry.Input.Released);
                    WriteInt32(stream, entry.Input.AxisX);
                    WriteInt32(stream, entry.Input.AxisY);
                    WriteInt32(stream, entry.FixedStepsSincePreviousVisual);
                    WriteString(stream, entry.SceneName);
                    WriteString(stream, entry.EventName);
                    WriteString(stream, entry.RngStateSha256);
                }

                var end = stream.Position;
                stream.Position = countPosition;
                WriteInt32(stream, count);
                stream.Position = end;
                return Sha256Utility.ComputeHex(stream.ToArray());
            }
        }

        internal static string RequireSha256(string value, string name)
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }

            if (value.Length != 64)
            {
                throw new ArgumentException(
                    "SHA-256 must contain exactly 64 lowercase hex characters.",
                    name);
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= '0' && character <= '9')
                      || (character >= 'a' && character <= 'f')))
                {
                    throw new ArgumentException(
                        "SHA-256 must contain exactly 64 lowercase hex characters.",
                        name);
                }
            }

            return value;
        }

        internal static string RequireOptionalSha256(string value, string name)
        {
            if (string.Equals(
                    value,
                    VerificationLedgerEntry.RngNotCaptured,
                    StringComparison.Ordinal))
            {
                return value;
            }

            return RequireSha256(value, name);
        }

        private static void WriteString(Stream stream, string value)
        {
            var bytes = StrictUtf8.GetBytes(value);
            WriteInt32(stream, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteInt32(Stream stream, int value)
        {
            unchecked
            {
                stream.WriteByte((byte)((uint)value >> 24));
                stream.WriteByte((byte)((uint)value >> 16));
                stream.WriteByte((byte)((uint)value >> 8));
                stream.WriteByte((byte)value);
            }
        }

        private static void WriteInt64(Stream stream, long value)
        {
            unchecked
            {
                var unsigned = (ulong)value;
                stream.WriteByte((byte)(unsigned >> 56));
                stream.WriteByte((byte)(unsigned >> 48));
                stream.WriteByte((byte)(unsigned >> 40));
                stream.WriteByte((byte)(unsigned >> 32));
                stream.WriteByte((byte)(unsigned >> 24));
                stream.WriteByte((byte)(unsigned >> 16));
                stream.WriteByte((byte)(unsigned >> 8));
                stream.WriteByte((byte)unsigned);
            }
        }
    }
}
