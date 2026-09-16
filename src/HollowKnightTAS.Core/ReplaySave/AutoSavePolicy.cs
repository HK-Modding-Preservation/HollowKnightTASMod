using System;

namespace HollowKnightTAS.Core.ReplaySave
{
    public sealed class AutoSavePolicy : IEquatable<AutoSavePolicy>
    {
        public const long DefaultIntervalMovieTicks = 18000;
        public const int DefaultRetentionCount = 20;
        public const long MaximumIntervalMovieTicks = 1000000000;
        public const int MaximumRetentionCount = 1000;

        public AutoSavePolicy(
            bool enabled,
            long intervalMovieTicks = DefaultIntervalMovieTicks,
            int retentionCount = DefaultRetentionCount)
        {
            if (intervalMovieTicks <= 0
                || intervalMovieTicks > MaximumIntervalMovieTicks)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(intervalMovieTicks),
                    intervalMovieTicks,
                    "Auto-save interval must be in [1, 1000000000] movie ticks.");
            }

            if (retentionCount <= 0
                || retentionCount > MaximumRetentionCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(retentionCount),
                    retentionCount,
                    "Auto-save retention must be in [1, 1000].");
            }

            Enabled = enabled;
            IntervalMovieTicks = intervalMovieTicks;
            RetentionCount = retentionCount;
        }

        public bool Enabled { get; }
        public long IntervalMovieTicks { get; }
        public int RetentionCount { get; }

        public static AutoSavePolicy Default =>
            new AutoSavePolicy(
                true,
                DefaultIntervalMovieTicks,
                DefaultRetentionCount);

        public bool Equals(AutoSavePolicy? other)
        {
            return other != null
                   && Enabled == other.Enabled
                   && IntervalMovieTicks == other.IntervalMovieTicks
                   && RetentionCount == other.RetentionCount;
        }

        public override bool Equals(object? value)
        {
            return Equals(value as AutoSavePolicy);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Enabled.GetHashCode();
                hash = (hash * 397) ^ IntervalMovieTicks.GetHashCode();
                hash = (hash * 397) ^ RetentionCount;
                return hash;
            }
        }
    }

    public sealed class AutoSavePolicyResult
    {
        public AutoSavePolicyResult(
            bool success,
            AutoSavePolicy policy,
            string error)
        {
            Success = success;
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            Error = error ?? string.Empty;
        }

        public bool Success { get; }
        public AutoSavePolicy Policy { get; }
        public string Error { get; }
    }
}
