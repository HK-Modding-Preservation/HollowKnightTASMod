using System;
using System.Diagnostics;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace HollowKnightTAS.Companion.Services
{
    /// <summary>Controller-owned pre-frame handshake, independent of Runtime IPC.</summary>
    public sealed class StartupBootGate : IDisposable
    {
        private readonly EventWaitHandle ready;
        private readonly EventWaitHandle proceed;
        private bool disposed;
        private bool acknowledged;
        private readonly EventWaitHandle? step;
        private readonly MemoryMappedFile? state;
        private readonly MemoryMappedViewAccessor? stateView;
        private int expectedFrame;

        public StartupBootGate(bool frameBased = false)
        {
            Token = Guid.NewGuid().ToString("N");
            ready = new EventWaitHandle(false, EventResetMode.ManualReset,
                "Local\\HKTAS.Boot." + Token + ".Ready", out var readyCreated);
            try
            {
                proceed = new EventWaitHandle(false, EventResetMode.ManualReset,
                    "Local\\HKTAS.Boot." + Token + ".Continue", out var proceedCreated);
                if (!readyCreated || !proceedCreated)
                {
                    proceed.Dispose();
                    throw new InvalidOperationException("Startup gate identity already exists.");
                }
                if (frameBased)
                {
                    step = new EventWaitHandle(false, EventResetMode.AutoReset,
                        "Local\\HKTAS.Boot." + Token + ".Step", out var stepCreated);
                    if (!stepCreated) throw new InvalidOperationException("Startup step identity already exists.");
                    state = MemoryMappedFile.CreateNew("Local\\HKTAS.Boot." + Token + ".State", 16);
                    stateView = state.CreateViewAccessor();
                }
            }
            catch
            {
                stateView?.Dispose(); state?.Dispose(); step?.Dispose();
                proceed?.Dispose(); ready.Dispose(); throw;
            }
        }

        public string Token { get; }
        public bool IsFrameBased => stateView != null;
        public int CompletedFrames => !disposed && stateView != null ? stateView.ReadInt32(0) : -1;
        public bool IsAcknowledged
        {
            get
            {
                if (!disposed && ready.WaitOne(0)) acknowledged = true;
                return acknowledged;
            }
        }
        public bool IsWaiting => !disposed && IsAcknowledged && ready.WaitOne(0) && !proceed.WaitOne(0)
            && (stateView == null || (stateView.ReadInt32(12) == 1 && stateView.ReadInt32(4) == 1
                && CompletedFrames >= expectedFrame));

        public void ConfigureInjector(ProcessStartInfo start)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            start.Environment["HKTAS_BOOT_GATE_TOKEN"] = Token;
            start.Environment["HKTAS_BOOT_GATE_OWNER"] = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
            start.Environment.Remove("HKTAS_BOOT_FRAME_GATE");
            if (IsFrameBased) start.Environment["HKTAS_BOOT_FRAME_GATE"] = "1";
        }

        public void Step()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!IsFrameBased || !IsWaiting) throw new InvalidOperationException("启动帧尚未停稳，不能单步。");
            expectedFrame = checked(CompletedFrames + 1);
            ready.Reset();
            step!.Set();
        }

        public void Continue()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            proceed.Set();
        }

        public void Dispose()
        {
            if (disposed) return;
            acknowledged |= ready.WaitOne(0);
            disposed = true;
            proceed.Set();
            proceed.Dispose();
            ready.Dispose();
            step?.Dispose();
            stateView?.Dispose();
            state?.Dispose();
        }
    }
}
