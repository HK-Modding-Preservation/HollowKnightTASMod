using System;
using System.IO;
using HollowKnightTAS.Core.ReplaySave;

namespace HollowKnightTAS.Companion.Services
{
    public static class PendingSlotRecovery
    {
        // Caller holds GameLaunchGate. Pending legacy recovery is reported,
        // never applied to the player's original files during launch.
        public static int RecoverBeforeLaunch()
        {
            var saveRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", "Team Cherry", "Hollow Knight");
            return RecoverBeforeLaunch(saveRoot);
        }

        public static int RecoverBeforeLaunch(string saveRoot)
        {
            if (string.IsNullOrWhiteSpace(saveRoot)) throw new ArgumentException("Explicit save root required.", nameof(saveRoot));
            var store = new SlotRecoveryStore(Path.Combine(saveRoot, "HollowKnightTAS", "replay-saves", "v1", "slot-recovery"));
            foreach (var owner in store.FindPendingOwners())
            {
                throw new InvalidOperationException("Pending slot recovery blocked TAS launch for "
                    + owner.OperationId + ". Original save files and recovery records were preserved; "
                    + "resolve the legacy recovery record manually before launching.");
            }
            return 0;
        }
    }
}
