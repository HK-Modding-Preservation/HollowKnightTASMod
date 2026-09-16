using System;

namespace HollowKnightTAS.Companion.Services
{
    /// <summary>UI-thread-owned lifecycle for the early native handshake, not a frame counter.</summary>
    public sealed class StartupBootController : IDisposable
    {
        private StartupBootGate? gate;
        private bool waiting;
        private int completedFrames = -1;
        public event EventHandler? Changed;
        public bool IsPending => gate != null;
        public bool IsWaiting => waiting;
        public bool CanStep => waiting && gate?.IsFrameBased == true;
        public int CompletedFrames => completedFrames;

        public StartupBootGate Begin(bool frameBased = true)
        {
            if (gate != null) throw new InvalidOperationException("启动接管尚未完成。");
            gate = new StartupBootGate(frameBased);
            Changed?.Invoke(this, EventArgs.Empty);
            return gate;
        }

        public void Refresh()
        {
            var next = gate?.IsWaiting == true;
            var frames = gate?.CompletedFrames ?? -1;
            if (waiting == next && completedFrames == frames) return;
            waiting = next;
            completedFrames = frames;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Step()
        {
            Refresh();
            if (!CanStep) throw new InvalidOperationException("启动帧尚未停稳，不能单步。");
            gate!.Step();
            Refresh();
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
            completedFrames = -1;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
