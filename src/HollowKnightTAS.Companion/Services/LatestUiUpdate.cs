using System;

namespace HollowKnightTAS.Companion.Services
{
    // At most one queued display update, even when the UI is temporarily busy.
    public sealed class LatestUiUpdate
    {
        private readonly object sync = new();
        private Action? latest;
        public void Post(Action action, Action<Action> dispatch)
        {
            lock (sync)
            {
                var alreadyQueued = latest != null;
                latest = action;
                if (alreadyQueued) return;
            }
            dispatch(() =>
            {
                Action? update;
                lock (sync) { update = latest; latest = null; }
                update?.Invoke();
            });
        }
    }
}
