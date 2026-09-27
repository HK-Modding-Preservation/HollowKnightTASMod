using System;
using System.IO;
using System.Text;

namespace HollowKnightTAS.Core.Media
{
    /// <summary>Streaming RGB/float PCM Matroska, one timestamped BlockGroup per game frame.
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
                E(0x1654AE6B, Join(E(0xAE, Join(U(0xD7, 1), U(0x73C5, 1), U(0x83, 1), U(0x9C, 0),
                    S(0x86, "V_UNCOMPRESSED"),
                    U(0x23E383, (ulong)(1000000000L * f.FpsDenominator / f.FpsNumerator)),
                    E(0xE0, Join(U(0xB0, (ulong)f.Width), U(0xBA, (ulong)f.Height),
                        E(0x2EB524, new byte[] { 82, 71, 66, 24 }))))),
                    E(0xAE, Join(U(0xD7, 2), U(0x73C5, 2), U(0x83, 2), U(0x9C, 0),
                        S(0x86, "A_PCM/FLOAT/IEEE"), E(0xE1, Join(F(0xB5, f.SampleRate),
                            U(0x9F, (ulong)f.Channels), U(0x6264, 32))))))));
        }

        public static byte[] Frame(byte[] rgb, byte[] pcm, long startMicroseconds, long durationMicroseconds, long audioStartMicroseconds)
        {
            if (startMicroseconds < 0 || durationMicroseconds <= 0) throw new ArgumentOutOfRangeException();
            var offset = checked((short)(audioStartMicroseconds - startMicroseconds));
            var timestamp = U(0xE7, (ulong)startMicroseconds);
            var block = Join(ElementHeader(0xA1, checked(rgb.Length + 4)), new byte[] { 0x81, 0, 0, 0 });
            var duration = U(0x9B, (ulong)durationMicroseconds);
            var group = ElementHeader(0xA0, checked(block.Length + rgb.Length + duration.Length));
            var audio = pcm.Length == 0 ? Array.Empty<byte>() : Join(ElementHeader(0xA3, checked(pcm.Length + 4)),
                new byte[] { 0x82, (byte)(offset >> 8), (byte)offset, 0x80 });
            var cluster = ElementHeader(0x1F43B675, checked(timestamp.Length + group.Length + block.Length
                + rgb.Length + duration.Length + audio.Length + pcm.Length));
            // Copy the large image only once; metadata composition must not multiply frame allocations.
            return Join(cluster, timestamp, group, block, rgb, duration, audio, pcm);
        }

        private static byte[] F(uint id, double value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return E(id, bytes);
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
            => Join(ElementHeader(id, data.Length), data);

        private static byte[] ElementHeader(uint id, int length)
        {
            using (var stream = new MemoryStream())
            {
                var n = id > 0xffffff ? 4 : id > 0xffff ? 3 : id > 0xff ? 2 : 1;
                for (var i = n - 1; i >= 0; i--) stream.WriteByte((byte)(id >> (8 * i)));
                var size = (ulong)length;
                n = 1;
                while (size >= ((1UL << (7 * n)) - 1)) n++;
                size |= 1UL << (7 * n);
                for (var i = n - 1; i >= 0; i--) stream.WriteByte((byte)(size >> (8 * i)));
                return stream.ToArray();
            }
        }
        private static byte[] Join(params byte[][] parts)
        {
            var length = 0;
            foreach (var part in parts) length = checked(length + part.Length);
            var result = new byte[length];
            var offset = 0;
            foreach (var part in parts) { Buffer.BlockCopy(part, 0, result, offset, part.Length); offset += part.Length; }
            return result;
        }
    }
}
