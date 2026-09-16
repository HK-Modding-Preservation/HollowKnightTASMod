using System;
using System.Diagnostics;
using System.IO;
using HollowKnightTAS.Core.ReplaySave;

namespace HollowKnightTAS.Companion.Services
{
    public static class PendingSlotRecovery
    {
        // Caller holds GameLaunchGate. This never terminates a process.
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
            var restored = 0;
            foreach (var owner in store.FindPendingOwners())
            {
                bool MayWrite()
                {
                    var games = Process.GetProcessesByName("hollow_knight");
                    try { if (games.Length != 0) return false; }
                    finally { foreach (var game in games) game.Dispose(); }
                    try
                    {
                        using var process = Process.GetProcessById(owner.ProcessId);
                        // Fail closed for both a live owner and PID reuse. No
                        // inference from a stale on-disk "completed" status.
                        return process.HasExited;
                    }
                    catch (ArgumentException) { return true; }
                }
                try { restored += store.RecoverAfterExit(owner.OperationId, owner.ProcessId,
                    owner.ProcessStartedUtc, saveRoot, MayWrite); }
                catch (Exception exception)
                {
                    throw new InvalidOperationException("Pending slot recovery blocked TAS launch for "
                        + owner.OperationId + ". Current files and recovery records were preserved. " + exception.Message, exception);
                }
            }
            return restored;
        }
    }
}
