using System;

namespace HollowKnightTAS.Runtime.Companion
{
    public enum CompanionLaunchState
    {
        Disabled = 0,
        Idle = 1,
        Discovering = 2,
        Verifying = 3,
        AttachingExisting = 4,
        Launching = 5,
        AwaitingHandshake = 6,
        Ready = 7,
        Unavailable = 8,
        Rejected = 9,
        VersionConflict = 10,
        Backoff = 11,
        CircuitOpen = 12,
        Stopped = 13
    }

    public sealed class CompanionLaunchSnapshot
    {
        public CompanionLaunchSnapshot(
            CompanionLaunchState state,
            string detail,
            int recentFailureCount,
            int? ownedProcessId,
            string bundleVersion,
            DateTimeOffset? nextRetryUtc)
        {
            State = state;
            Detail = detail;
            RecentFailureCount = recentFailureCount;
            OwnedProcessId = ownedProcessId;
            BundleVersion = bundleVersion;
            NextRetryUtc = nextRetryUtc;
        }

        public CompanionLaunchState State { get; }
        public string Detail { get; }
        public int RecentFailureCount { get; }
        public int? OwnedProcessId { get; }
        public string BundleVersion { get; }
        public DateTimeOffset? NextRetryUtc { get; }
    }
}
