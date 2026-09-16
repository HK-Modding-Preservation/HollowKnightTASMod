using System;

namespace HollowKnightTAS.Core.Media
{
    /// <summary>Fixed-size RGB24 video and interleaved PCM audio sent to the encoder.</summary>
    public sealed class VideoExportFormat
    {
        public VideoExportFormat(int width, int height, int fpsNumerator = 60,
            int fpsDenominator = 1, int sampleRate = 48000, int channels = 2)
        {
            if (width < 2 || width > 8192 || width % 2 != 0)
                throw new ArgumentOutOfRangeException(nameof(width), "MP4 width must be even and between 2 and 8192.");
            if (height < 2 || height > 8192 || height % 2 != 0)
                throw new ArgumentOutOfRangeException(nameof(height), "MP4 height must be even and between 2 and 8192.");
            if (fpsNumerator <= 0) throw new ArgumentOutOfRangeException(nameof(fpsNumerator));
            if (fpsDenominator <= 0) throw new ArgumentOutOfRangeException(nameof(fpsDenominator));
            if ((long)fpsNumerator < fpsDenominator || (long)fpsNumerator > 240L * fpsDenominator)
                throw new ArgumentOutOfRangeException(nameof(fpsNumerator), "Frame rate must be between 1 and 240 fps.");
            if (sampleRate < 8000 || sampleRate > 192000)
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (channels != 1 && channels != 2) throw new ArgumentOutOfRangeException(nameof(channels));

            var divisor = Gcd(fpsNumerator, fpsDenominator);
            Width = width;
            Height = height;
            FpsNumerator = fpsNumerator / divisor;
            FpsDenominator = fpsDenominator / divisor;
            SampleRate = sampleRate;
            Channels = channels;
        }

        public int Width { get; }
        public int Height { get; }
        public int FpsNumerator { get; }
        public int FpsDenominator { get; }
        public int SampleRate { get; }
        public int Channels { get; }
        public int VideoFrameBytes => checked(Width * Height * 3);

        private static int Gcd(int a, int b)
        {
            while (b != 0) { var remainder = a % b; a = b; b = remainder; }
            return a;
        }
    }
}
