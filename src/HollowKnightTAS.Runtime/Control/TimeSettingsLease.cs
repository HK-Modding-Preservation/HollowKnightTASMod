using System;
using HollowKnightTAS.Core.Ledger;
using UnityEngine;

namespace HollowKnightTAS.Runtime.Control
{
    public sealed class TimeSettingsSnapshot : IEquatable<TimeSettingsSnapshot>
    {
        private TimeSettingsSnapshot(
            int timeScaleBits,
            int fixedDeltaTimeBits,
            int targetFrameRate,
            int vSyncCount)
        {
            TimeScaleBits = timeScaleBits;
            FixedDeltaTimeBits = fixedDeltaTimeBits;
            TargetFrameRate = targetFrameRate;
            VSyncCount = vSyncCount;
        }

        public int TimeScaleBits { get; }
        public int FixedDeltaTimeBits { get; }
        public int TargetFrameRate { get; }
        public int VSyncCount { get; }
        public float TimeScale => SingleBits.ToSingle(TimeScaleBits);
        public float FixedDeltaTime =>
            SingleBits.ToSingle(FixedDeltaTimeBits);

        public static TimeSettingsSnapshot Capture()
        {
            return new TimeSettingsSnapshot(
                SingleBits.FromSingle(Time.timeScale),
                SingleBits.FromSingle(Time.fixedDeltaTime),
                Application.targetFrameRate,
                QualitySettings.vSyncCount);
        }

        public bool Equals(TimeSettingsSnapshot? other)
        {
            return other != null
                   && TimeScaleBits == other.TimeScaleBits
                   && FixedDeltaTimeBits == other.FixedDeltaTimeBits
                   && TargetFrameRate == other.TargetFrameRate
                   && VSyncCount == other.VSyncCount;
        }

        public override bool Equals(object? value)
        {
            return Equals(value as TimeSettingsSnapshot);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = TimeScaleBits;
                hash = (hash * 397) ^ FixedDeltaTimeBits;
                hash = (hash * 397) ^ TargetFrameRate;
                hash = (hash * 397) ^ VSyncCount;
                return hash;
            }
        }
    }

    public sealed class TimeSettingsRestoreReport
    {
        internal TimeSettingsRestoreReport(
            bool attempted,
            bool equivalent,
            bool timeScaleChanged,
            string reason,
            TimeSettingsSnapshot before,
            TimeSettingsSnapshot after)
        {
            Attempted = attempted;
            Equivalent = equivalent;
            TimeScaleChanged = timeScaleChanged;
            Reason = reason ?? string.Empty;
            Before = before ?? throw new ArgumentNullException(nameof(before));
            After = after ?? throw new ArgumentNullException(nameof(after));
        }

        public bool Attempted { get; }
        public bool Equivalent { get; }
        public bool TimeScaleChanged { get; }
        public string Reason { get; }
        public TimeSettingsSnapshot Before { get; }
        public TimeSettingsSnapshot After { get; }
    }

    public sealed class TimeSettingsLease : IDisposable
    {
        private bool timeScaleChanged;
        private bool restored;

        public TimeSettingsLease()
        {
            Before = TimeSettingsSnapshot.Capture();
            if (float.IsNaN(Before.TimeScale)
                || float.IsInfinity(Before.TimeScale)
                || Before.TimeScale <= 0f)
            {
                throw new InvalidOperationException(
                    "Pause control requires a positive finite original timeScale.");
            }
        }

        public TimeSettingsSnapshot Before { get; }
        public TimeSettingsRestoreReport? RestoreReport { get; private set; }
        public bool IsRestored => restored;

        public void SetPaused()
        {
            ThrowIfRestored();
            SetTimeScale(0f);
        }

        public void SetStepWindowOpen()
        {
            ThrowIfRestored();
            SetTimeScale(Before.TimeScale);
        }

        public TimeSettingsRestoreReport Restore(string reason)
        {
            if (restored)
            {
                return RestoreReport!;
            }

            if (timeScaleChanged)
            {
                Time.timeScale = Before.TimeScale;
            }

            var after = TimeSettingsSnapshot.Capture();
            RestoreReport = new TimeSettingsRestoreReport(
                true,
                Before.Equals(after),
                timeScaleChanged,
                reason,
                Before,
                after);
            restored = true;
            return RestoreReport;
        }

        public void Dispose()
        {
            Restore("dispose");
        }

        private void SetTimeScale(float value)
        {
            if (SingleBits.FromSingle(Time.timeScale)
                == SingleBits.FromSingle(value))
            {
                return;
            }

            Time.timeScale = value;
            timeScaleChanged = true;
        }

        private void ThrowIfRestored()
        {
            if (restored)
            {
                throw new ObjectDisposedException(nameof(TimeSettingsLease));
            }
        }
    }
}
