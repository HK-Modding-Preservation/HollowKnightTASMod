using System;
using System.Diagnostics;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.ReplaySave;

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
        private readonly EventWaitHandle? v2Command;
        private readonly MemoryMappedFile? state;
        private readonly MemoryMappedViewAccessor? stateView;
        private readonly MemoryMappedFile? v2State;
        private readonly MemoryMappedViewAccessor? v2View;
        private ProtectedSaveSession? protectedSaves;
        private readonly bool fullRun;
        private bool v2Armed;
        private long v2CommandSequence;
        private int expectedFrame;

        private const int V2StateBytes = 136;
        private const int V2Magic = 0x32544648;
        private const int V2ModePaused = 0;
        private const int V2ModeStep = 1;
        private const int V2ModeRun = 2;
        private const int V2ModeFault = 3;
        private const int V2ModeFinished = 4;

        public StartupBootGate(bool frameBased = false, bool fullRun = false)
        {
            if (fullRun && !frameBased)
                throw new ArgumentException("Full-run gate requires the native frame gate.", nameof(frameBased));
            this.fullRun = fullRun;
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
                    if (fullRun)
                    {
                        v2Command = new EventWaitHandle(false, EventResetMode.AutoReset,
                            "Local\\HKTAS.Boot." + Token + ".V2Command", out var v2Created);
                        if (!v2Created) throw new InvalidOperationException("Full-run command identity already exists.");
                        v2State = MemoryMappedFile.CreateNew("Local\\HKTAS.Boot." + Token + ".V2State",
                            V2StateBytes);
                        v2View = v2State.CreateViewAccessor();
                        v2View.Write(0, V2Magic);
                        v2View.Write(4, 2);
                        var tokenBytes = System.Text.Encoding.ASCII.GetBytes(Token);
                        v2View.WriteArray(8, tokenBytes, 0, tokenBytes.Length);
                        v2View.Write(76, V2ModePaused);
                    }
                }
            }
            catch
            {
                v2View?.Dispose(); v2State?.Dispose(); v2Command?.Dispose();
                stateView?.Dispose(); state?.Dispose(); step?.Dispose();
                proceed?.Dispose(); ready.Dispose(); throw;
            }
        }

        public string Token { get; }
        public bool IsFrameBased => stateView != null;
        public bool IsFullRun => fullRun;
        public int CompletedFrames => !disposed && stateView != null ? stateView.ReadInt32(0) : -1;
        public long NativeCompletedFrames => !disposed && v2View != null ? v2View.ReadInt64(40) : -1;
        public int SaveGuardArmed => !disposed && v2View != null ? v2View.ReadInt32(92) : 0;
        public int FullRunFaultCode => !disposed && v2View != null ? v2View.ReadInt32(88) : 0;
        public bool IsFullRunFinished => !disposed && v2View != null
            && v2View.ReadInt32(76) == V2ModeFinished;
        public bool IsCommandPending => !disposed && v2View != null
            && v2View.ReadInt64(48) != v2View.ReadInt64(56);
        public bool IsAcknowledged
        {
            get
            {
                if (!disposed && ready.WaitOne(0)) acknowledged = true;
                return acknowledged;
            }
        }
        public bool IsWaiting => !disposed && IsAcknowledged && ready.WaitOne(0)
            && (fullRun
                ? v2View != null && (v2View.ReadInt32(76) == V2ModePaused
                    || v2View.ReadInt32(76) == V2ModeFinished)
                    && !IsCommandPending
                    && v2View.ReadInt32(88) == 0 && v2View.ReadInt32(92) == 1
                : !proceed.WaitOne(0)
                    && (stateView == null || (stateView.ReadInt32(12) == 1
                        && stateView.ReadInt32(4) == 1 && CompletedFrames >= expectedFrame)));

        public void SetProtectedSaveSession(ProtectedSaveSession session)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!fullRun || protectedSaves != null || session == null)
                throw new InvalidOperationException("Protected save session is already set or unavailable.");
            protectedSaves = session;
        }

        public void ConfigureInjector(ProcessStartInfo start)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            start.Environment["HKTAS_BOOT_GATE_TOKEN"] = Token;
            start.Environment["HKTAS_BOOT_GATE_OWNER"] = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
            start.Environment.Remove("HKTAS_BOOT_FRAME_GATE");
            if (IsFrameBased) start.Environment["HKTAS_BOOT_FRAME_GATE"] = "1";
            start.Environment.Remove("HKTAS_FULL_RUN_V2");
            start.Environment.Remove("HKTAS_FULL_RUN_SAVE_GUARD");
            start.Environment.Remove("HKTAS_SAVE_GUARD_ORIGINAL_ROOT");
            start.Environment.Remove("HKTAS_SAVE_GUARD_TOKEN");
            start.Environment.Remove("HKTAS_SAVE_DESCRIPTOR_PATH");
            if (fullRun)
            {
                if (protectedSaves == null)
                    throw new InvalidOperationException("Full-run gate has no protected save session.");
                start.Environment["HKTAS_FULL_RUN_V2"] = "1";
                start.Environment["HKTAS_FULL_RUN_SAVE_GUARD"] = "1";
                start.Environment["HKTAS_SAVE_GUARD_ORIGINAL_ROOT"] = protectedSaves.Descriptor.OriginalRoot;
                start.Environment["HKTAS_SAVE_GUARD_TOKEN"] = protectedSaves.Descriptor.GuardToken;
                start.Environment["HKTAS_SAVE_DESCRIPTOR_PATH"] = protectedSaves.DescriptorPath;
            }
        }

        public void ArmV2(string token, string descriptorSha256)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!fullRun || v2View == null || v2Armed || token != Token
                || NativeCompletedFrames != 0 || !IsWaiting
                || !MovieProtocolV1.IsLowerSha256(descriptorSha256))
                throw new InvalidOperationException("Full-run bootstrap can only be armed once at paused frame 0.");
            var digest = Convert.FromHexString(descriptorSha256);
            v2View.WriteArray(96, digest, 0, digest.Length);
            Thread.MemoryBarrier();
            v2View.Write(80, 1);
            v2Armed = true;
        }

        public async Task<NativeFrameBoundary> StepV2Async(long expectedNativeFrame,
            CancellationToken cancellationToken)
        {
            if (!fullRun || !IsWaiting || NativeCompletedFrames != expectedNativeFrame)
                throw new InvalidOperationException("Full-run native frame is not paused at the expected position.");
            var sequence = RequestV2(V2ModeStep, expectedNativeFrame);
            return await WaitV2Async(sequence, -1, true, cancellationToken, 300)
                .ConfigureAwait(false);
        }

        public async Task<NativeFrameBoundary> PauseV2Async(CancellationToken cancellationToken)
        {
            if (!fullRun || v2View == null) throw new InvalidOperationException("Full-run gate is unavailable.");
            var pendingDeadline = Stopwatch.GetTimestamp() + 5L * Stopwatch.Frequency;
            while (v2View.ReadInt64(48) != v2View.ReadInt64(56)
                && Stopwatch.GetTimestamp() < pendingDeadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (v2View.ReadInt32(88) != 0)
                    return new NativeFrameBoundary(NativeCompletedFrames,
                        v2View.ReadInt64(56), "Fault", "Native full-run gate faulted.");
                await Task.Delay(5, cancellationToken).ConfigureAwait(false);
            }
            var sequence = RequestV2(V2ModePaused, NativeCompletedFrames);
            return await WaitV2Async(sequence, -1, true, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<NativeFrameBoundary> RunV2Async(long expectedNativeFrame,
            CancellationToken cancellationToken)
        {
            if (!fullRun || !IsWaiting || NativeCompletedFrames != expectedNativeFrame)
                throw new InvalidOperationException("Full-run native frame is not paused at the expected position.");
            var sequence = RequestV2(V2ModeRun, expectedNativeFrame);
            return await WaitV2Async(sequence, -1, false, cancellationToken)
                .ConfigureAwait(false);
        }

        private long RequestV2(int mode, long expectedNativeFrame)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!v2Armed || v2View == null || v2Command == null)
                throw new InvalidOperationException("Full-run bootstrap is not armed. Open a v2 Movie or create a new Movie at frame 0.");
            if (v2View.ReadInt32(88) != 0 || v2View.ReadInt32(76) == V2ModeFault)
                throw new InvalidOperationException("Native full-run gate fault " + v2View.ReadInt32(88) + ". Restart the session.");
            if (v2View.ReadInt32(76) == V2ModeFinished)
                throw new InvalidOperationException("Full-run Movie has finished. Restart the session to replay.");
            if (expectedNativeFrame < 0)
                throw new InvalidOperationException("Full-run native frame is unavailable.");
            if (IsCommandPending)
                throw new InvalidOperationException("Waiting for native command acknowledgement (command "
                    + v2View.ReadInt64(48) + ", acknowledged " + v2View.ReadInt64(56) + ").");
            var sequence = checked(++v2CommandSequence);
            v2View.Write(64, expectedNativeFrame);
            v2View.Write(72, mode);
            Thread.MemoryBarrier();
            v2View.Write(48, sequence);
            proceed.Set(); // Releases the native clock worker on the first command.
            v2Command.Set();
            return sequence;
        }

        private async Task<NativeFrameBoundary> WaitV2Async(long sequence, long requiredFrame,
            bool requirePause, CancellationToken cancellationToken, int timeoutSeconds = 30)
        {
            var deadline = Stopwatch.GetTimestamp() + timeoutSeconds * (long)Stopwatch.Frequency;
            while (Stopwatch.GetTimestamp() < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (disposed || v2View == null)
                    return new NativeFrameBoundary(-1, sequence, "Fault", "Gate disposed.");
                var frame = v2View.ReadInt64(40);
                var ack = v2View.ReadInt64(56);
                var mode = v2View.ReadInt32(76);
                var fault = v2View.ReadInt32(88);
                if (fault != 0 || mode == V2ModeFault)
                    return new NativeFrameBoundary(frame, ack, "Fault", "Native fault " + fault);
                if (ack == sequence && mode == V2ModeFinished && ready.WaitOne(0))
                    return new NativeFrameBoundary(frame, ack, "Completed", string.Empty);
                if (ack == sequence && (requiredFrame < 0 || frame == requiredFrame)
                    && (!requirePause || (mode == V2ModePaused && ready.WaitOne(0))))
                    return new NativeFrameBoundary(frame, ack, requirePause ? "Paused" : "Running", string.Empty);
                if (requiredFrame >= 0 && frame > requiredFrame)
                    return new NativeFrameBoundary(frame, ack, "Fault", "Native frame advanced past the command target.");
                await Task.Delay(5, cancellationToken).ConfigureAwait(false);
            }
            return new NativeFrameBoundary(NativeCompletedFrames, v2View?.ReadInt64(56) ?? -1,
                "Fault", "Native frame command timed out (command " + sequence
                    + ", acknowledged " + (v2View?.ReadInt64(56) ?? -1)
                    + ", native frame " + NativeCompletedFrames + "). Restart the session.");
        }

        public void Step()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (fullRun) throw new InvalidOperationException("Use StepV2Async for a full-run gate.");
            if (!IsFrameBased || !IsWaiting) throw new InvalidOperationException("启动帧尚未停稳，不能单步。");
            expectedFrame = checked(CompletedFrames + 1);
            ready.Reset();
            step!.Set();
        }

        public void Continue()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (fullRun) throw new InvalidOperationException("Use RunV2Async for a full-run gate.");
            proceed.Set();
        }

        public void Dispose()
        {
            if (disposed) return;
            acknowledged |= ready.WaitOne(0);
            disposed = true;
            if (v2View != null)
            {
                v2View.Write(88, 90);
                v2View.Write(76, V2ModeFault);
                v2Command?.Set();
            }
            proceed.Set();
            proceed.Dispose();
            ready.Dispose();
            step?.Dispose();
            v2Command?.Dispose();
            v2View?.Dispose();
            v2State?.Dispose();
            stateView?.Dispose();
            state?.Dispose();
        }
    }

    public sealed class NativeFrameBoundary
    {
        public NativeFrameBoundary(long completedFrame, long ackSequence, string mode, string error)
        {
            CompletedFrame = completedFrame;
            AckSequence = ackSequence;
            Mode = mode ?? throw new ArgumentNullException(nameof(mode));
            Error = error ?? throw new ArgumentNullException(nameof(error));
        }

        public long CompletedFrame { get; }
        public long AckSequence { get; }
        public string Mode { get; }
        public string Error { get; }
    }
}
