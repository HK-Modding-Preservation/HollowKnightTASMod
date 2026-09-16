using System;

namespace HollowKnightTAS.Companion.Services
{
    /// <summary>UI-thread-owned lifecycle for the early native handshake, not a frame counter.</summary>
    public sealed class StartupBootController : IDisposable
    {
        private StartupBootGate? gate;
        private bool waiting;
        public event EventHandler? Changed;
        public bool IsPending => gate != null;
        public bool IsWaiting => waiting;

        public StartupBootGate Begin()
        {
            if (gate != null) throw new InvalidOperationException("启动接管尚未完成。");
            gate = new StartupBootGate();
            Changed?.Invoke(this, EventArgs.Empty);
            return gate;
        }

        public void Refresh()
        {
            var next = gate?.IsWaiting == true;
            if (waiting == next) return;
            waiting = next;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Continue()
        {
            Refresh();
            if (!waiting) throw new InvalidOperationException("原生启动暂停尚未确认，不能继续。");
            Dispose();
        }

        public void Dispose()
        {
            gate?.Dispose();
            gate = null;
            waiting = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
