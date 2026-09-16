using System;

namespace HollowKnightTAS.Core.Media
{
    /// <summary>Splits whole interleaved DSP blocks into exact media-frame sample counts.</summary>
    public sealed class AudioBlockReframer
    {
        private readonly int blockValues;
        private float[] pending = Array.Empty<float>();

        public AudioBlockReframer(int blockSampleFrames, int channels)
        {
            if (blockSampleFrames < 1 || blockSampleFrames > 8192)
                throw new ArgumentOutOfRangeException(nameof(blockSampleFrames));
            if (channels < 1 || channels > 2) throw new ArgumentOutOfRangeException(nameof(channels));
            Channels = channels;
            blockValues = checked(blockSampleFrames * channels);
        }

        public int Channels { get; }
        public int PendingValues => pending.Length;

        public int RequiredRenderValues(int outputValues)
        {
            if (outputValues < 0 || outputValues % Channels != 0)
                throw new ArgumentOutOfRangeException(nameof(outputValues));
            var missing = Math.Max(0L, (long)outputValues - pending.Length);
            return checked((int)((missing + blockValues - 1) / blockValues * blockValues));
        }

        public float[] Consume(float[] rendered, int outputValues)
        {
            if (rendered == null) throw new ArgumentNullException(nameof(rendered));
            if (rendered.Length != RequiredRenderValues(outputValues))
                throw new ArgumentException("Render must supply exactly the requested whole DSP blocks.", nameof(rendered));
            var output = new float[outputValues];
            var fromPending = Math.Min(outputValues, pending.Length);
            Array.Copy(pending, 0, output, 0, fromPending);
            var fromRendered = outputValues - fromPending;
            Array.Copy(rendered, 0, output, fromPending, fromRendered);
            var remainder = new float[pending.Length + rendered.Length - outputValues];
            Array.Copy(pending, fromPending, remainder, 0, pending.Length - fromPending);
            Array.Copy(rendered, fromRendered, remainder, pending.Length - fromPending, rendered.Length - fromRendered);
            pending = remainder;
            return output;
        }
    }
}
