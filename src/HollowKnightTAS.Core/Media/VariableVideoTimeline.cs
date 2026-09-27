using System;

namespace HollowKnightTAS.Core.Media
{
    /// <summary>Accumulates game time, never wall time. Rounds cumulative boundaries only.</summary>
    public sealed class VariableVideoTimeline
    {
        private decimal seconds;
        public long Microseconds { get; private set; }
        public long AudioSampleFrames { get; private set; }
        public int Advance(double duration, int sampleRate, int channels)
        {
            if (double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 0)
                throw new ArgumentOutOfRangeException(nameof(duration));
            seconds += (decimal)duration;
            Microseconds = checked((long)decimal.Round(seconds * 1000000m, 0, MidpointRounding.AwayFromZero));
            var samples = checked((long)decimal.Floor(seconds * sampleRate + 0.00000001m));
            var values = checked((int)((samples - AudioSampleFrames) * channels));
            AudioSampleFrames = samples;
            return values;
        }
    }
}
