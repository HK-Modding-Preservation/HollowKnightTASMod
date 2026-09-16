using System;

namespace HollowKnightTAS.Core.Ipc
{
    // Shared with ClockPayload by source link; no dependency on the game.
    public static class RecordingRootConfiguration
    {
        public const double PreparationLeadSeconds = 30d;

        public static double Choose(double currentGameTime, float fixedDeltaTime = 0.02f)
        {
            if (double.IsNaN(currentGameTime) || double.IsInfinity(currentGameTime)
                || currentGameTime < 0d)
                throw new ArgumentOutOfRangeException(nameof(currentGameTime));
            if (float.IsNaN(fixedDeltaTime) || float.IsInfinity(fixedDeltaTime)
                || fixedDeltaTime <= 0f)
                throw new ArgumentOutOfRangeException(nameof(fixedDeltaTime));
            // Choose a point on the source clock's actual update lattice, not
            // a whole second that may be unreachable after a scene load.
            var frames = Math.Ceiling(PreparationLeadSeconds / fixedDeltaTime);
            var projected = currentGameTime + frames * (double)fixedDeltaTime;
            var target = (double)(float)projected;
            Validate(target);
            return target;
        }

        public static void Validate(double target)
        {
            // The existing phase normalizer compares float Time values. Do not
            // silently round a stored double or accept a time too coarse for
            // the profile's 50 Hz clock. Such sessions need a different profile.
            if (double.IsNaN(target) || double.IsInfinity(target) || target <= 0d
                || (double)(float)target != target
                || (float)(target + 0.02d) == (float)target)
                throw new ArgumentOutOfRangeException(nameof(target),
                    "Recording root must be an exactly representable positive float time at 50 Hz precision.");
        }

        public static bool IsValidPhysicsPhase(double gameTime, double fixedTime, double fixedDeltaTime)
        {
            if (double.IsNaN(gameTime) || double.IsInfinity(gameTime)
                || double.IsNaN(fixedTime) || double.IsInfinity(fixedTime)
                || double.IsNaN(fixedDeltaTime) || double.IsInfinity(fixedDeltaTime)
                || fixedTime < 0 || fixedDeltaTime <= 0)
                return false;
            var remainder = gameTime - fixedTime;
            return remainder >= 0 && remainder < fixedDeltaTime;
        }
    }
}
