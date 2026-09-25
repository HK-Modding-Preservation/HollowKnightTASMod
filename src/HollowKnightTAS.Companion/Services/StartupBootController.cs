using System;
using System.Threading;
using System.Threading.Tasks;

namespace HollowKnightTAS.Companion.Services
{
    /// <summary>UI-thread-owned lifecycle for the early native handshake, not a frame counter.</summary>
    public sealed class StartupBootController : IDisposable
    {
        private StartupBootGate? gate;
        private bool waiting;
        private bool commandPending;
        private int completedFrames = -1;
        public event EventHandler? Changed;
        public bool IsPending => gate != null;
        public bool IsWaiting => waiting;
        public bool CanStep => waiting && gate?.IsFrameBased == true;
        public bool IsCommandPending => gate?.IsCommandPending == true;
        public int CompletedFrames => completedFrames;
        public long NativeCompletedFrames => gate?.NativeCompletedFrames ?? -1;
        public int FullRunFaultCode => gate?.FullRunFaultCode ?? 0;

        public StartupBootGate Begin(bool frameBased = true)
        {
            if (gate != null) throw new InvalidOperationException("启动接管尚未完成。");
            gate = new StartupBootGate(frameBased);
            Changed?.Invoke(this, EventArgs.Empty);
            return gate;
        }

        public StartupBootGate BeginV2()
        {
            if (gate != null) throw new InvalidOperationException("启动接管尚未完成。");
            gate = new StartupBootGate(frameBased: true, fullRun: true);
            Changed?.Invoke(this, EventArgs.Empty);
            return gate;
        }

        public void ArmV2(string token, string descriptorSha256)
        {
            gate?.ArmV2(token, descriptorSha256);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public async Task<NativeFrameBoundary> StepV2Async(long expectedFrame,
            CancellationToken cancellationToken)
        {
            var command = gate?.StepV2Async(expectedFrame, cancellationToken)
                ?? throw new InvalidOperationException("Full-run gate is unavailable.");
            Refresh();
            try { return await command; }
            finally { Refresh(); }
        }

        public Task<NativeFrameBoundary> PauseV2Async(CancellationToken cancellationToken)
            => gate?.PauseV2Async(cancellationToken)
               ?? throw new InvalidOperationException("Full-run gate is unavailable.");

        public async Task<NativeFrameBoundary> RunV2Async(long expectedFrame,
            CancellationToken cancellationToken)
        {
            var command = (gate ?? throw new InvalidOperationException("Full-run gate is unavailable."))
                .RunV2Async(expectedFrame, cancellationToken);
            Refresh();
            try { return await command; }
            finally { Refresh(); }
        }

        public void Refresh()
        {
            var next = gate?.IsWaiting == true;
            var pending = IsCommandPending;
            var frames = gate?.CompletedFrames ?? -1;
            if (waiting == next && completedFrames == frames && commandPending == pending) return;
            waiting = next;
            commandPending = pending;
            completedFrames = frames;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Step()
        {
            if (gate?.IsFullRun == true)
                throw new InvalidOperationException("Use v2 frame step for a full-run movie.");
            Refresh();
            if (!CanStep) throw new InvalidOperationException("启动帧尚未停稳，不能单步。");
            gate!.Step();
            Refresh();
        }

        public void Continue()
        {
            if (gate?.IsFullRun == true)
                throw new InvalidOperationException("Use v2 frame run for a full-run movie.");
            Refresh();
            if (!waiting) throw new InvalidOperationException("原生启动暂停尚未确认，不能继续。");
            Dispose();
        }

        public void Dispose()
        {
            gate?.Dispose();
            gate = null;
            waiting = false;
            commandPending = false;
            completedFrames = -1;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
