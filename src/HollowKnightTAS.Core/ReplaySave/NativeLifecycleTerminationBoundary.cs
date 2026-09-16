namespace HollowKnightTAS.Core.ReplaySave
{
    public static class NativeLifecycleTerminationBoundary
    {
        public static bool CanExitFailedSource(bool failed, double elapsedSeconds)
            => failed && elapsedSeconds >= 120;

        public static bool CanPause(bool nativeStarted, bool nativeCompleted, bool loadingSlot,
            bool loadSucceeded, int callbackFrame, int currentFrame, bool stableMenu, bool stableGameplay)
        {
            if (!nativeStarted) return stableMenu || stableGameplay;
            if (!nativeCompleted) return false;
            if (!loadingSlot) return stableMenu;
            // A successful load callback may fire while the old menu is still
            // visible. Wait for ContinueGame to reach gameplay before cleanup.
            return loadSucceeded ? stableGameplay : stableMenu && currentFrame > callbackFrame;
        }
    }
}
