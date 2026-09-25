using System;

namespace HollowKnightTAS.Core.Ipc
{
    public static class StartupActivationPolicy
    {
        public const string ManualSessionPrefix = "manual-startup-";

        public static bool IsManualRequest(string sessionId) =>
            sessionId.StartsWith(ManualSessionPrefix, StringComparison.Ordinal);

        public static bool ShouldStartRuntime(bool protectedLaunch, string? startupLatch) =>
            protectedLaunch || startupLatch == "1";
    }
}
