using System;
using System.Numerics;

namespace HollowKnightTAS.Core.Media
{
    /// <summary>Media time, independent of elapsed wall time and encoder throughput.</summary>
    public sealed class VideoExportTimeline
    {
        public VideoExportTimeline(VideoExportFormat format)
        {
            Format = format ?? throw new ArgumentNullException(nameof(format));
        }

        public VideoExportFormat Format { get; }

        /// <summary>Per-channel sample frames through N video frames, rounded down once.</summary>
        public long AudioSampleFramesThrough(long videoFrameCount)
        {
            if (videoFrameCount < 0) throw new ArgumentOutOfRangeException(nameof(videoFrameCount));
            // BigInteger avoids intermediate overflow even when the final result fits Int64.
            var samples = (BigInteger)videoFrameCount * Format.SampleRate * Format.FpsDenominator
                / Format.FpsNumerator;
            return checked((long)samples);
        }

        /// <summary>Number of interleaved float values belonging to this zero-based frame.</summary>
        public int AudioValueCountForFrame(long frameIndex)
        {
            if (frameIndex < 0) throw new ArgumentOutOfRangeException(nameof(frameIndex));
            var samples = AudioSampleFramesThrough(checked(frameIndex + 1)) - AudioSampleFramesThrough(frameIndex);
            return checked((int)(samples * Format.Channels));
        }
    }
}
