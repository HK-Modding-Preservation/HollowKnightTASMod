using HollowKnightTAS.Core.Ledger;
using UnityEngine;

namespace HollowKnightTAS.Runtime.ReplaySave
{
    /// <summary>
    /// Original-runtime tick eligibility only. The former timing lease wrote
    /// captureDeltaTime, targetFrameRate and vSync on the TAS side and was
    /// retired by T24 because those writes changed the gameplay environment.
    /// </summary>
    public static class ReplayDeterministicTimingLease
    {
        public const string ProfileId = "none-original-runtime-v1";

        public static bool MovieTicksMayAdvance()
        {
            return SingleBits.FromSingle(Time.timeScale)
                   == SingleBits.FromSingle(1f);
        }
    }
}
