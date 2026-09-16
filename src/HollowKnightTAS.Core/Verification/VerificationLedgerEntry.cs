using System;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Ledger;

namespace HollowKnightTAS.Core.Verification
{
    public sealed class VerificationLedgerEntry
    {
        public const string RngNotCaptured = "not-captured";

        public VerificationLedgerEntry(
            long movieTick,
            TickStamp stamp,
            InputSample input,
            int fixedStepsSincePreviousVisual,
            int timeMinusFixedTimeBits,
            string sceneName,
            string eventName,
            string rngStateSha256 = RngNotCaptured)
        {
            if (movieTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(movieTick));
            }

            if (fixedStepsSincePreviousVisual < -1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fixedStepsSincePreviousVisual));
            }

            MovieTick = movieTick;
            Stamp = stamp;
            Input = input;
            FixedStepsSincePreviousVisual = fixedStepsSincePreviousVisual;
            TimeMinusFixedTimeBits = timeMinusFixedTimeBits;
            SceneName = sceneName
                        ?? throw new ArgumentNullException(nameof(sceneName));
            EventName = eventName
                        ?? throw new ArgumentNullException(nameof(eventName));
            RngStateSha256 = RunSignature.RequireOptionalSha256(
                rngStateSha256,
                nameof(rngStateSha256));
        }

        public long MovieTick { get; }
        public TickStamp Stamp { get; }
        public InputSample Input { get; }
        public int FixedStepsSincePreviousVisual { get; }
        public int TimeMinusFixedTimeBits { get; }
        public string SceneName { get; }
        public string EventName { get; }
        public string RngStateSha256 { get; }
    }
}
