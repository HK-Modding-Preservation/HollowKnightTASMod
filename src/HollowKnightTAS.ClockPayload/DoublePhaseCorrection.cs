using System;
using System.Runtime.InteropServices;

namespace HollowKnightTAS.ClockPayload
{
    /// <summary>
    /// Computes a positive Float32 capture delta that approaches an exact
    /// Double-time fixed-step boundary without rounding past it.  This type
    /// has no Unity dependency so the exact arithmetic is unit-testable.
    /// </summary>
    internal static class DoublePhaseCorrection
    {
        public static bool TryCalculate(
            double residual,
            float fixedDeltaTime,
            out float correction,
            out bool adjustedToPredecessor,
            out int faultCode)
        {
            correction = 0f;
            adjustedToPredecessor = false;
            faultCode = 0;

            if (double.IsNaN(residual)
                || double.IsInfinity(residual)
                || float.IsNaN(fixedDeltaTime)
                || float.IsInfinity(fixedDeltaTime)
                || fixedDeltaTime <= 0f)
            {
                faultCode = -3;
                return false;
            }

            var fixedDeltaTimeDouble = (double)fixedDeltaTime;
            var remainderPhase = residual % fixedDeltaTimeDouble;
            if (remainderPhase < 0d)
            {
                remainderPhase += fixedDeltaTimeDouble;
            }
            var target = fixedDeltaTimeDouble - remainderPhase;
            var candidate = (float)target;
            var candidateDouble = (double)candidate;

            // If nearest rounding crosses the mathematical boundary, use the
            // immediately preceding positive Float32.  Also do this for a
            // non-zero residual whose modulo correction is a full fixed
            // delta; submitting the full delta would preserve that residual.
            if (candidateDouble > target
                || (candidateDouble == target
                    && target == fixedDeltaTimeDouble
                    && BitConverter.DoubleToInt64Bits(residual) != 0L))
            {
                var candidateBits = SingleBits.FromSingle(candidate);
                if (candidateBits <= 1)
                {
                    faultCode = -4;
                    return false;
                }
                candidate = SingleBits.ToSingle(candidateBits - 1);
                candidateDouble = (double)candidate;
                adjustedToPredecessor = true;
            }

            if (double.IsNaN(target)
                || double.IsInfinity(target)
                || target <= 0d
                || target > fixedDeltaTimeDouble
                || float.IsNaN(candidate)
                || float.IsInfinity(candidate)
                || candidate <= 0f
                || candidate > fixedDeltaTime
                || candidateDouble > target)
            {
                faultCode = -3;
                return false;
            }

            correction = candidate;
            return true;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct SingleBits
        {
            [FieldOffset(0)]
            private float single;

            [FieldOffset(0)]
            private int integer;

            public static int FromSingle(float value)
            {
                return new SingleBits { single = value }.integer;
            }

            public static float ToSingle(int value)
            {
                return new SingleBits { integer = value }.single;
            }
        }
    }
}
