namespace HollowKnightTAS.ClockPayload
{
    internal static class RecordingClockCalibrationPolicy
    {
        // A scene name is never permission to recalibrate an established
        // gameplay timeline. Explicit preparation must also precede its root.
        internal static bool IsAllowed(bool explicitlyRequested, bool rootStarted, bool fixtureScene)
        {
            return !rootStarted && (explicitlyRequested || fixtureScene);
        }
    }
}
