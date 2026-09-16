using System;
using System.Globalization;

namespace HollowKnightTAS.Core.Automation
{
    public static class StateMutationBounds
    {
        public const float MaximumAbsolutePosition = 10000f;
        public const float MaximumPositionDelta = 20f;
        public const float MaximumAbsoluteVelocity = 100f;

        public static bool TryParseFiniteSingle(
            string value,
            out float result)
        {
            return float.TryParse(
                       value,
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out result)
                   && !float.IsNaN(result)
                   && !float.IsInfinity(result);
        }
    }
}
