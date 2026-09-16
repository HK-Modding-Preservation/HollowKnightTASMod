using System;
using System.Text;

namespace HollowKnightTAS.Core.ReplaySave
{
    public enum RecordingOriginAlignment { Waiting, Matched, Mismatch }
    // Persisted observations, not proof of replay equivalence. Profile semantics
    // define the RNG root and frame phase; elapsed frames bind the journal to it.
    public sealed class RecordingOrigin
    {
        public RecordingOrigin(string profileId, double rootBoundarySeconds,
            int framesAfterRootBoundary, double gameTimeSeconds, double fixedTimeSeconds)
        {
            if (string.IsNullOrWhiteSpace(profileId)
                || Encoding.UTF8.GetByteCount(profileId) > 256)
                throw new ArgumentException("A bounded startup profile ID is required.", nameof(profileId));
            RequireTime(rootBoundarySeconds, nameof(rootBoundarySeconds));
            RequireTime(gameTimeSeconds, nameof(gameTimeSeconds));
            RequireTime(fixedTimeSeconds, nameof(fixedTimeSeconds));
            if (framesAfterRootBoundary < 0)
                throw new ArgumentOutOfRangeException(nameof(framesAfterRootBoundary));
            if (gameTimeSeconds < rootBoundarySeconds || fixedTimeSeconds < rootBoundarySeconds)
                throw new ArgumentException("Journal origin cannot precede its recording root.");
            ProfileId = profileId;
            RootBoundarySeconds = rootBoundarySeconds;
            FramesAfterRootBoundary = framesAfterRootBoundary;
            GameTimeSeconds = gameTimeSeconds;
            FixedTimeSeconds = fixedTimeSeconds;
        }

        public string ProfileId { get; }
        public double RootBoundarySeconds { get; }
        public int FramesAfterRootBoundary { get; }
        public double GameTimeSeconds { get; }
        public double FixedTimeSeconds { get; }

        public RecordingOriginAlignment CompareBoundary(int framesAfterRootBoundary,
            double gameTimeSeconds, double fixedTimeSeconds)
        {
            RequireTime(gameTimeSeconds, nameof(gameTimeSeconds));
            RequireTime(fixedTimeSeconds, nameof(fixedTimeSeconds));
            if (framesAfterRootBoundary < 0)
                throw new ArgumentOutOfRangeException(nameof(framesAfterRootBoundary));
            if (framesAfterRootBoundary < FramesAfterRootBoundary)
                return RecordingOriginAlignment.Waiting;
            if (framesAfterRootBoundary != FramesAfterRootBoundary
                || BitConverter.DoubleToInt64Bits(gameTimeSeconds)
                   != BitConverter.DoubleToInt64Bits(GameTimeSeconds)
                || BitConverter.DoubleToInt64Bits(fixedTimeSeconds)
                   != BitConverter.DoubleToInt64Bits(FixedTimeSeconds))
                return RecordingOriginAlignment.Mismatch;
            return RecordingOriginAlignment.Matched;
        }

        private static void RequireTime(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0
                || BitConverter.DoubleToInt64Bits(value) == long.MinValue)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
