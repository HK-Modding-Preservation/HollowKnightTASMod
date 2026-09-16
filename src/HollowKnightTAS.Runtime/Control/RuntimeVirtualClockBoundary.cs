using System;

namespace HollowKnightTAS.Runtime.Control
{
    /// <summary>
    /// Fixed extension point used by the authenticated external clock payload.
    /// It never supplies time values and cannot advance gameplay. Its only
    /// operation brackets wall-clock time spent with the Unity main thread
    /// blocked at a completed-frame TAS boundary.
    /// </summary>
    public static class RuntimeVirtualClockBoundary
    {
        public const string RequiredProviderId =
            "native.clock.pause-wall-time-exclusion.experimental.v5";
        private static readonly object Sync = new object();
        private static string providerId = string.Empty;
        private static Action? beginPause;
        private static Action? endPause;

        public static bool IsAvailable
        {
            get
            {
                lock (Sync)
                {
                    return beginPause != null && endPause != null;
                }
            }
        }

        public static string ProviderId
        {
            get
            {
                lock (Sync)
                {
                    return providerId;
                }
            }
        }

        public static bool Register(
            string value,
            Action begin,
            Action end)
        {
            if (!string.Equals(
                    value,
                    RequiredProviderId,
                    StringComparison.Ordinal)
                || begin == null
                || end == null)
            {
                return false;
            }

            lock (Sync)
            {
                if (beginPause != null || endPause != null)
                {
                    return string.Equals(
                        providerId,
                        value,
                        StringComparison.Ordinal);
                }

                providerId = value;
                beginPause = begin;
                endPause = end;
                return true;
            }
        }

        public static bool Unregister(string value)
        {
            lock (Sync)
            {
                if (!string.Equals(
                        providerId,
                        value,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                providerId = string.Empty;
                beginPause = null;
                endPause = null;
                return true;
            }
        }

        internal static void BeginPause()
        {
            Action callback;
            lock (Sync)
            {
                callback = beginPause
                           ?? throw new InvalidOperationException(
                               "The required virtual-clock provider is unavailable.");
            }
            callback();
        }

        internal static void EndPause()
        {
            Action callback;
            lock (Sync)
            {
                callback = endPause
                           ?? throw new InvalidOperationException(
                               "The required virtual-clock provider is unavailable.");
            }
            callback();
        }
    }
}
