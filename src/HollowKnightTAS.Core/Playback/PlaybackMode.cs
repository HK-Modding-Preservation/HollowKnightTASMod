namespace HollowKnightTAS.Core.Playback
{
    public enum PlaybackMode : byte
    {
        Idle = 0,
        Recording = 1,
        Replaying = 2,
        Stopping = 3,
        Faulted = 4
    }

    public enum PlaybackStopReason : byte
    {
        Completed = 0,
        Manual = 1,
        Emergency = 2,
        SceneChanged = 3,
        HeroUnavailable = 4,
        ManifestMismatch = 5,
        BaselineMismatch = 6,
        AssertionFailed = 7,
        AdapterFault = 8,
        RuntimeFault = 9
    }
}
