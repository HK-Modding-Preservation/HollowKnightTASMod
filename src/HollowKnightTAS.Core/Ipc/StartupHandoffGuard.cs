using System;

namespace HollowKnightTAS.Core.Ipc
{
    /// <summary>A one-shot title-screen handoff, never a way to close an active save.</summary>
    public sealed class StartupHandoffGuard
    {
        private bool gameplayObserved;
        private string? prepared;
        private bool committed;

        public void ObserveGameplay(bool active) { gameplayObserved |= active; }

        public string Prepare(string operationId, bool controlledStartup, bool stableTitle)
        {
            if (controlledStartup) return "already-controlled";
            if (gameplayObserved || committed) return "ineligible";
            if (!stableTitle) return "wait-for-title";
            if (!IpcIdentifier.IsValid(operationId, 96)) throw new ArgumentException("Invalid startup handoff identity.");
            if (prepared != null && prepared != operationId) return "ineligible";
            prepared = operationId;
            return "prepared";
        }

        public void Commit(string operationId, bool controlledStartup, bool stableTitle)
        {
            if (controlledStartup || gameplayObserved || !stableTitle || committed
                || prepared == null || prepared != operationId)
                throw new InvalidOperationException("Startup handoff is no longer safe or does not match its preparation.");
            committed = true;
        }
    }
}
