using System;

namespace HollowKnightTAS.Core.ReplaySave
{
    // Installed HK calls AfterSavegameLoad(data) before SavegameLoad(slot).
    // A slot is confirmed only after the data callback has been observed once.
    public sealed class NativeLoadHookProgress
    {
        private readonly int slot;
        private bool dataLoaded;
        public NativeLoadHookProgress(int slot)
        {
            if (slot < 1 || slot > 4) throw new ArgumentOutOfRangeException(nameof(slot));
            this.slot = slot;
        }
        public bool IsComplete { get; private set; }
        public string Failure { get; private set; } = string.Empty;

        public void ObserveDataLoaded()
        {
            Require(Failure.Length == 0 && !dataLoaded && !IsComplete,
                "Unexpected or repeated native data-load callback.");
            dataLoaded = true;
        }

        public void ObserveSlotConfirmed(int actualSlot)
        {
            Require(Failure.Length == 0 && dataLoaded && !IsComplete && actualSlot == slot,
                "Native slot callback is out of order, repeated, or for a different slot.");
            IsComplete = true;
        }

        private void Require(bool condition, string reason)
        {
            if (condition) return;
            if (Failure.Length == 0) Failure = reason;
            IsComplete = false;
            throw new InvalidOperationException(Failure);
        }
    }
}
