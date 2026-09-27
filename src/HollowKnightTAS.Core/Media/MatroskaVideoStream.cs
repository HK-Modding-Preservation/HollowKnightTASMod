using System;
using System.IO;
using System.Text;

namespace HollowKnightTAS.Core.Media
{
    /// <summary>Streaming RGB Matroska, one timestamped BlockGroup per game frame.
    /// TimestampScale=1000 ns. BlockDuration also preserves the final frame duration.
    /// Specification: https://www.matroska.org/technical/elements.html
    /// </summary>
    public static class MatroskaVideoStream
    {
        public static byte[] Header(VideoExportFormat f)
        {
            return Join(E(0x1A45DFA3, Join(U(0x4286, 1), U(0x42F7, 1), U(0x42F2, 4), U(0x42F3, 8),
                S(0x4282, "matroska"), U(0x4287, 4), U(0x4285, 2))),
                new byte[] { 0x18, 0x53, 0x80, 0x67, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff },
                E(0x1549A966, Join(U(0x2AD7B1, 1000), S(0x4D80, "HollowKnightTAS"), S(0x5741, "HollowKnightTAS"))),
                E(0x1654AE6B, E(0xAE, Join(U(0xD7, 1), U(0x73C5, 1), U(0x83, 1), U(0x9C, 0),
                    S(0x86, "V_UNCOMPRESSED"),
                    U(0x23E383, (ulong)(1000000000L * f.FpsDenominator / f.FpsNumerator)),
                    E(0xE0, Join(U(0xB0, (ulong)f.Width), U(0xBA, (ulong)f.Height),
                        E(0x2EB524, new byte[] { 82, 71, 66, 24 })))))));
        }

        public static byte[] Frame(byte[] rgb, long startMicroseconds, long durationMicroseconds)
        {
            if (startMicroseconds < 0 || durationMicroseconds <= 0) throw new ArgumentOutOfRangeException();
            return E(0x1F43B675, Join(U(0xE7, (ulong)startMicroseconds),
                E(0xA0, Join(E(0xA1, Join(new byte[] { 0x81, 0, 0, 0 }, rgb)),
                    U(0x9B, (ulong)durationMicroseconds)))));
        }

        private static byte[] S(uint id, string value) => E(id, Encoding.UTF8.GetBytes(value));
        private static byte[] U(uint id, ulong value)
        {
            var n = 1;
            while (n < 8 && (value >> (8 * n)) != 0) n++;
            var bytes = new byte[n];
            for (var i = n - 1; i >= 0; i--) { bytes[i] = (byte)value; value >>= 8; }
            return E(id, bytes);
        }
        private static byte[] E(uint id, byte[] data)
        {
            using (var stream = new MemoryStream())
            {
                var n = id > 0xffffff ? 4 : id > 0xffff ? 3 : id > 0xff ? 2 : 1;
                for (var i = n - 1; i >= 0; i--) stream.WriteByte((byte)(id >> (8 * i)));
                var size = (ulong)data.Length;
                n = 1;
                while (size >= ((1UL << (7 * n)) - 1)) n++;
                size |= 1UL << (7 * n);
                for (var i = n - 1; i >= 0; i--) stream.WriteByte((byte)(size >> (8 * i)));
                stream.Write(data, 0, data.Length);
                return stream.ToArray();
            }
        }
        private static byte[] Join(params byte[][] parts)
        {
            using (var stream = new MemoryStream())
            {
                foreach (var part in parts) stream.Write(part, 0, part.Length);
                return stream.ToArray();
            }
        }
    }
}
