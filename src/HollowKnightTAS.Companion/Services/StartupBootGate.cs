using System;
using System.Diagnostics;
using System.Globalization;
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

        public StartupBootGate()
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
            }
            catch { ready.Dispose(); throw; }
        }

        public string Token { get; }
        public bool IsAcknowledged => disposed ? acknowledged : ready.WaitOne(0);
        public bool IsWaiting => !disposed && ready.WaitOne(0) && !proceed.WaitOne(0);

        public void ConfigureInjector(ProcessStartInfo start)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            start.Environment["HKTAS_BOOT_GATE_TOKEN"] = Token;
            start.Environment["HKTAS_BOOT_GATE_OWNER"] = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        }

        public void Continue()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            proceed.Set();
        }

        public void Dispose()
        {
            if (disposed) return;
            acknowledged = ready.WaitOne(0);
            disposed = true;
            proceed.Set();
            proceed.Dispose();
            ready.Dispose();
        }
    }
}
