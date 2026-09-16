using System;
using System.Threading;

namespace HollowKnightTAS.Runtime.Verification
{
    public static class RuntimeVerificationEligibility
    {
        private static int debugMutationApplied;

        public static bool IsEligible =>
            Volatile.Read(ref debugMutationApplied) == 0;

        public static string Status =>
            IsEligible
                ? "Eligible"
                : "NonVerifiableDebugMutation";

        public static void MarkDebugMutationApplied()
        {
            Interlocked.Exchange(ref debugMutationApplied, 1);
        }

        public static void ThrowIfIneligible()
        {
            if (!IsEligible)
            {
                throw new InvalidOperationException(
                    "NonVerifiableDebugMutation: a successful typed "
                    + "debug mutation permanently disqualifies this "
                    + "process from T07/T16 verification.");
            }
        }
    }
}
