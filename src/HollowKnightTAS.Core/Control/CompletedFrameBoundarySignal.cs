using System;

namespace HollowKnightTAS.Core.Control
{
    /// <summary>
    /// Process-local notification that Unity has completed the rendered frame.
    /// The runtime publishes immediately before its completed-frame command
    /// gate can block; read-only verification observers use the same signal as
    /// their WaitForEndOfFrame coroutine. With no subscribers this is a no-op.
    /// </summary>
    public static class CompletedFrameBoundarySignal
    {
        public const string BoundaryId =
            "post-render-completed-frame-sampling-v1";

        public static event Action? Reached;

        public static void Publish()
        {
            Reached?.Invoke();
        }
    }
}
