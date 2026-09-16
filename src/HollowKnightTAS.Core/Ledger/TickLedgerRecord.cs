using System;
using System.Runtime.InteropServices;

namespace HollowKnightTAS.Core.Ledger
{
    public sealed class TickLedgerRecord
    {
        public TickLedgerRecord(
            long sequence,
            string sessionId,
            string manifestSha256,
            string runId,
            string profile,
            TickStamp stamp,
            int fixedStepsSincePreviousVisual,
            float time,
            float fixedTime,
            float timeMinusFixedTime,
            float deltaTime,
            float unscaledDeltaTime,
            float timeScale,
            float realtimeSinceStartup,
            string sceneName,
            string detail)
        {
            if (sequence <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence));
            }

            SessionId = RequireText(sessionId, nameof(sessionId));
            ManifestSha256 = RequireText(manifestSha256, nameof(manifestSha256));
            RunId = RequireText(runId, nameof(runId));
            Profile = RequireText(profile, nameof(profile));
            SceneName = sceneName ?? throw new ArgumentNullException(nameof(sceneName));
            Detail = detail ?? throw new ArgumentNullException(nameof(detail));

            if (stamp.Phase == TickPhase.VisualUpdateBegin)
            {
                if (fixedStepsSincePreviousVisual < 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(fixedStepsSincePreviousVisual),
                        "VisualUpdateBegin requires a non-negative fixed-step count.");
                }
            }
            else if (fixedStepsSincePreviousVisual != -1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fixedStepsSincePreviousVisual),
                    "Non-visual phases must use -1 for the fixed-step count.");
            }

            Sequence = sequence;
            Stamp = stamp;
            FixedStepsSincePreviousVisual = fixedStepsSincePreviousVisual;
            TimeBits = SingleBits.FromSingle(time);
            FixedTimeBits = SingleBits.FromSingle(fixedTime);
            TimeMinusFixedTimeBits = SingleBits.FromSingle(timeMinusFixedTime);
            DeltaTimeBits = SingleBits.FromSingle(deltaTime);
            UnscaledDeltaTimeBits = SingleBits.FromSingle(unscaledDeltaTime);
            TimeScaleBits = SingleBits.FromSingle(timeScale);
            RealtimeSinceStartupBits = SingleBits.FromSingle(realtimeSinceStartup);
        }

        public long Sequence { get; }
        public string SessionId { get; }
        public string ManifestSha256 { get; }
        public string RunId { get; }
        public string Profile { get; }
        public TickStamp Stamp { get; }
        public int FixedStepsSincePreviousVisual { get; }
        public int TimeBits { get; }
        public int FixedTimeBits { get; }
        public int TimeMinusFixedTimeBits { get; }
        public int DeltaTimeBits { get; }
        public int UnscaledDeltaTimeBits { get; }
        public int TimeScaleBits { get; }
        public int RealtimeSinceStartupBits { get; }
        public string SceneName { get; }
        public string Detail { get; }

        private static string RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A non-empty value is required.", name);
            }

            return value;
        }
    }

    public static class SingleBits
    {
        public static int FromSingle(float value)
        {
            var union = new SingleInt32Union { Single = value };
            return union.Int32;
        }

        public static float ToSingle(int value)
        {
            var union = new SingleInt32Union { Int32 = value };
            return union.Single;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct SingleInt32Union
        {
            [FieldOffset(0)]
            public float Single;

            [FieldOffset(0)]
            public int Int32;
        }
    }
}
