using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Core.Deployment;
using HollowKnightTAS.Core.Ipc;
using HollowKnightTAS.Runtime.Ipc;

namespace HollowKnightTAS.Runtime.Companion
{
    public sealed class CompanionLauncher : IDisposable
    {
        private readonly object sync = new object();
        private readonly string modRoot;
        private readonly CompanionSessionRegistration registration;
        private readonly NamedPipeRuntimeServer server;
        private readonly CompanionControlClient control;
        private readonly CompanionLaunchBackoff backoff =
            new CompanionLaunchBackoff();
        private readonly CancellationTokenSource lifetime =
            new CancellationTokenSource();
        private readonly TimeSpan handshakeTimeout;
        private readonly Action<string> logInfo;
        private readonly Action<string> logWarning;
        private readonly Action<
            string,
            IReadOnlyDictionary<string, string>> emit;
        private CancellationTokenSource? runCancellation;
        private Task? runTask;
        private Process? ownedProcess;
        private bool desired;
        private int disposed;
        private CompanionLaunchSnapshot snapshot =
            new CompanionLaunchSnapshot(
                CompanionLaunchState.Idle,
                "Companion auto-start is idle.",
                0,
                null,
                string.Empty,
                null);

        public CompanionLauncher(
            string modRoot,
            CompanionSessionRegistration registration,
            NamedPipeRuntimeServer server,
            TimeSpan handshakeTimeout,
            Action<string> logInfo,
            Action<string> logWarning,
            Action<
                string,
                IReadOnlyDictionary<string, string>> emit)
        {
            this.modRoot = Path.GetFullPath(
                modRoot
                ?? throw new ArgumentNullException(nameof(modRoot)));
            this.registration = registration
                                ?? throw new ArgumentNullException(
                                    nameof(registration));
            this.server = server
                          ?? throw new ArgumentNullException(
                              nameof(server));
            if (handshakeTimeout < TimeSpan.FromSeconds(1)
                || handshakeTimeout > TimeSpan.FromSeconds(30))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(handshakeTimeout));
            }

            this.handshakeTimeout = handshakeTimeout;
            this.logInfo = logInfo;
            this.logWarning = logWarning;
            this.emit = emit;
            var identity =
                CompanionInstanceNames
                    .GetCurrentUserIdentity();
            control = new CompanionControlClient(
                CompanionInstanceNames.CreateControlPipeName(
                    identity));
        }

        public event Action<CompanionLaunchSnapshot>?
            StatusChanged;

        public CompanionLaunchSnapshot Snapshot
        {
            get
            {
                lock (sync)
                {
                    return snapshot;
                }
            }
        }

        public void StartAuto()
        {
            Start(manual: false);
        }

        public void ManualStartOrReconnect()
        {
            backoff.Reset();
            Start(manual: true);
        }

        public void DisableForSession()
        {
            lock (sync)
            {
                desired = false;
                runCancellation?.Cancel();
            }

            server.DisconnectCurrent();
            Transition(
                CompanionLaunchState.Disabled,
                "Companion disabled for this game session.",
                0,
                null,
                string.Empty);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            lock (sync)
            {
                desired = false;
                runCancellation?.Cancel();
            }

            lifetime.Cancel();
            try
            {
                runTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }

            ownedProcess?.Dispose();
            ownedProcess = null;
            runCancellation?.Dispose();
            lifetime.Dispose();
            Transition(
                CompanionLaunchState.Stopped,
                "Companion launcher stopped.",
                0,
                null,
                string.Empty);
        }

        public Task<bool> RequestSessionShutdownAsync(
            bool requestCompanionExit,
            CancellationToken cancellationToken)
        {
            return control.ShutdownAsync(
                registration,
                requestCompanionExit,
                TimeSpan.FromSeconds(1),
                cancellationToken);
        }

        private void Start(bool manual)
        {
            ThrowIfDisposed();
            lock (sync)
            {
                desired = true;
                if (server.IsConnected)
                {
                    TransitionUnderLock(
                        CompanionLaunchState.Ready,
                        manual
                            ? "Companion is already connected."
                            : "Companion connection is ready.",
                        0,
                        null,
                        snapshot.BundleVersion);
                    return;
                }

                if (runTask != null && !runTask.IsCompleted)
                {
                    return;
                }

                runCancellation?.Dispose();
                runCancellation =
                    CancellationTokenSource
                        .CreateLinkedTokenSource(
                            lifetime.Token);
                var token = runCancellation.Token;
                runTask = Task.Run(
                    () => RunLoopAsync(token),
                    token);
            }
        }

        private async Task RunLoopAsync(
            CancellationToken token)
        {
            while (!token.IsCancellationRequested
                   && IsDesired())
            {
                AttemptResult result;
                try
                {
                    result = await AttemptAsync(token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    result = AttemptResult.Failed(
                        exception.GetType().Name
                        + ": "
                        + exception.Message);
                }

                if (result.Terminal)
                {
                    return;
                }

                if (result.Ready)
                {
                    var connectedAt = DateTimeOffset.UtcNow;
                    while (!token.IsCancellationRequested
                           && IsDesired()
                           && server.IsConnected)
                    {
                        await Task.Delay(250, token)
                            .ConfigureAwait(false);
                    }

                    if (token.IsCancellationRequested
                        || !IsDesired())
                    {
                        return;
                    }

                    if (DateTimeOffset.UtcNow - connectedAt
                        >= TimeSpan.FromSeconds(60))
                    {
                        backoff.Reset();
                    }

                    result = AttemptResult.Failed(
                        "Companion connection ended.");
                }

                var decision = backoff.RegisterFailure(
                    DateTimeOffset.UtcNow);
                if (decision.CircuitOpen)
                {
                    Transition(
                        CompanionLaunchState.CircuitOpen,
                        "Five launch/connection failures occurred "
                        + "within five minutes. Press F10 to retry.",
                        decision.RecentFailureCount,
                        null,
                        result.BundleVersion);
                    return;
                }

                var retryAt =
                    DateTimeOffset.UtcNow + decision.Delay;
                Transition(
                    CompanionLaunchState.Backoff,
                    result.Detail,
                    decision.RecentFailureCount,
                    retryAt,
                    result.BundleVersion);
                await Task.Delay(decision.Delay, token)
                    .ConfigureAwait(false);
            }
        }

        private async Task<AttemptResult> AttemptAsync(
            CancellationToken token)
        {
            Transition(
                CompanionLaunchState.Discovering,
                "Discovering the fixed Companion bundle.",
                0,
                null,
                string.Empty);
            if (Environment.OSVersion.Platform
                    != PlatformID.Win32NT
                || !Environment.Is64BitOperatingSystem)
            {
                Transition(
                    CompanionLaunchState.Unavailable,
                    "Companion v1 requires Windows x64.",
                    0,
                    null,
                    string.Empty);
                return AttemptResult.TerminalFailure(
                    "Unsupported platform.");
            }

            var entrypoint = Path.Combine(
                modRoot,
                "Companion",
                "win-x64",
                "HollowKnightTAS.Companion.exe");
            if (!File.Exists(entrypoint))
            {
                Transition(
                    CompanionLaunchState.Unavailable,
                    "Companion executable is not installed.",
                    0,
                    null,
                    string.Empty);
                return AttemptResult.TerminalFailure(
                    "Companion executable is missing: " + entrypoint);
            }

            // Launch the local installation directly. Protocol compatibility is
            // negotiated by the existing connection handshake.
            var bundleVersion = CompanionProtocolMetadata.Version;
            Transition(
                CompanionLaunchState.AttachingExisting,
                "Checking the current-user Companion control pipe.",
                0,
                null,
                bundleVersion);
            var attach = await control.RegisterAsync(
                    registration,
                    TimeSpan.FromMilliseconds(750),
                    token)
                .ConfigureAwait(false);
            if (attach.Status
                == CompanionAttachStatus.Incompatible)
            {
                Transition(
                    CompanionLaunchState.VersionConflict,
                    attach.Detail,
                    0,
                    null,
                    bundleVersion);
                return AttemptResult.TerminalFailure(
                    attach.Detail);
            }

            if (attach.Status
                == CompanionAttachStatus.Rejected)
            {
                return AttemptResult.Failed(
                    attach.Detail,
                    bundleVersion);
            }

            if (attach.Status
                == CompanionAttachStatus.Attached)
            {
                Transition(
                    CompanionLaunchState.AwaitingHandshake,
                    "Existing Companion accepted this game session.",
                    0,
                    null,
                    attach.Version);
                if (await WaitForRuntimeConnectionAsync(
                        null,
                        token)
                    .ConfigureAwait(false))
                {
                    Transition(
                        CompanionLaunchState.Ready,
                        "Reused compatible Companion instance "
                        + attach.InstanceId + ".",
                        0,
                        null,
                        attach.Version);
                    return AttemptResult.Succeeded(
                        attach.Version);
                }

                return AttemptResult.Failed(
                    "Existing Companion did not authenticate in time.",
                    attach.Version);
            }

            return await LaunchAsync(
                    entrypoint,
                    bundleVersion,
                    token)
                .ConfigureAwait(false);
        }

        private async Task<AttemptResult> LaunchAsync(
            string entrypoint,
            string bundleVersion,
            CancellationToken token)
        {
            Transition(
                CompanionLaunchState.Launching,
                "Starting the Companion executable.",
                0,
                null,
                bundleVersion);
            Process? process = null;
            var bootstrapName =
                "HollowKnightTAS.Bootstrap."
                + Guid.NewGuid().ToString("N");
            using (var bootstrap =
                   new NativeNamedPipeServer(
                       bootstrapName,
                       PipeDirection.Out))
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = entrypoint,
                    WorkingDirectory =
                        Path.GetDirectoryName(entrypoint)
                        ?? modRoot,
                    Arguments =
                        "--bootstrap-pipe="
                        + bootstrapName,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    RedirectStandardError = true,
                    StandardErrorEncoding =
                        new UTF8Encoding(false, true)
                };
                process = Process.Start(startInfo);
                if (process == null)
                {
                    return AttemptResult.Failed(
                        "Process.Start returned no process.",
                        bundleVersion);
                }

                ReplaceOwnedProcess(process);
                if (!await WaitForBootstrapConnectionAsync(
                        bootstrap,
                        process,
                        token)
                    .ConfigureAwait(false))
                {
                    var exited = HasExited(process);
                    var bootstrapDiagnostic =
                        TryTerminateFailedOwnedProcess(process);
                    return AttemptResult.Failed(
                        (exited
                            ? "Companion exited before bootstrap."
                            : "Companion bootstrap timed out.")
                        + FormatDiagnostic(bootstrapDiagnostic),
                        bundleVersion);
                }

                using (var writeTimeout =
                       CancellationTokenSource
                           .CreateLinkedTokenSource(token))
                {
                    writeTimeout.CancelAfter(
                        handshakeTimeout);
                        await IpcCodec.WriteFrameAsync(
                            bootstrap.WriteStream,
                            new IpcEnvelope(
                                CompanionProtocolMetadata
                                    .ProtocolMaximum,
                                registration.SessionId,
                                0,
                                IpcMessageTypes.RegisterSession,
                                registration.ToPayload()),
                            writeTimeout.Token)
                        .ConfigureAwait(false);
                }
            }

            Transition(
                CompanionLaunchState.AwaitingHandshake,
                "Waiting for Companion authentication.",
                0,
                null,
                bundleVersion);
            if (await WaitForRuntimeConnectionAsync(
                    process,
                    token)
                .ConfigureAwait(false))
            {
                Transition(
                    CompanionLaunchState.Ready,
                    "Companion authenticated.",
                    0,
                    null,
                    bundleVersion);
                logInfo(
                    "T12 Companion ready pid="
                    + process.Id.ToString(
                        CultureInfo.InvariantCulture)
                    + " version="
                    + bundleVersion);
                return AttemptResult.Succeeded(
                    bundleVersion);
            }

            var exitedBeforeTermination = HasExited(process);
            var authenticationDiagnostic =
                TryTerminateFailedOwnedProcess(process);
            var detail = (exitedBeforeTermination
                ? "Companion exited before authentication."
                : "Companion authentication timed out.")
                + FormatDiagnostic(authenticationDiagnostic);
            logWarning("T12 " + detail);
            return AttemptResult.Failed(
                detail,
                bundleVersion);
        }

        private async Task<bool> WaitForRuntimeConnectionAsync(
            Process? process,
            CancellationToken token)
        {
            var deadline =
                DateTimeOffset.UtcNow + handshakeTimeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                if (server.IsConnected)
                {
                    return true;
                }

                if (process != null && HasExited(process))
                {
                    return false;
                }

                await Task.Delay(25, token)
                    .ConfigureAwait(false);
            }

            return server.IsConnected;
        }

        private async Task<bool> WaitForBootstrapConnectionAsync(
            NativeNamedPipeServer bootstrap,
            Process process,
            CancellationToken token)
        {
            var waitTask = Task.Run(
                () => bootstrap.WaitForConnection());
            var deadline =
                DateTimeOffset.UtcNow + handshakeTimeout;
            while (!waitTask.IsCompleted
                   && DateTimeOffset.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                if (HasExited(process))
                {
                    bootstrap.Dispose();
                    try
                    {
                        await waitTask.ConfigureAwait(false);
                    }
                    catch
                    {
                    }

                    return false;
                }

                await Task.Delay(25, token)
                    .ConfigureAwait(false);
            }

            if (!waitTask.IsCompleted)
            {
                bootstrap.Dispose();
                try
                {
                    await waitTask.ConfigureAwait(false);
                }
                catch
                {
                }

                return false;
            }

            await waitTask.ConfigureAwait(false);
            return bootstrap.IsConnected;
        }

        private void ReplaceOwnedProcess(Process process)
        {
            var old = Interlocked.Exchange(
                ref ownedProcess,
                process);
            old?.Dispose();
        }

        private string TryTerminateFailedOwnedProcess(
            Process process)
        {
            if (!ReferenceEquals(
                    Interlocked.CompareExchange(
                        ref ownedProcess,
                        null,
                        process),
                    process))
            {
                return string.Empty;
            }

            var diagnostic = string.Empty;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(1000);
                }
                diagnostic =
                    process.StandardError.ReadToEnd();
            }
            catch (Exception exception)
            {
                logWarning(
                    "T12 could not terminate its failed child: "
                    + exception.Message);
            }
            finally
            {
                process.Dispose();
            }

            return diagnostic;
        }

        private static string FormatDiagnostic(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var sanitized = value
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\\', '/');
            return " diagnostic="
                   + sanitized.Substring(
                       0,
                       Math.Min(320, sanitized.Length));
        }

        private static bool HasExited(Process process)
        {
            try
            {
                return process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        private bool IsDesired()
        {
            lock (sync)
            {
                return desired;
            }
        }

        private void Transition(
            CompanionLaunchState state,
            string detail,
            int failures,
            DateTimeOffset? nextRetry,
            string bundleVersion)
        {
            CompanionLaunchSnapshot value;
            lock (sync)
            {
                value = TransitionUnderLock(
                    state,
                    detail,
                    failures,
                    nextRetry,
                    bundleVersion);
            }

            PublishTransition(value);
        }

        private CompanionLaunchSnapshot TransitionUnderLock(
            CompanionLaunchState state,
            string detail,
            int failures,
            DateTimeOffset? nextRetry,
            string bundleVersion)
        {
            int? processId = null;
            var process = ownedProcess;
            if (process != null && !HasExited(process))
            {
                processId = process.Id;
            }

            snapshot = new CompanionLaunchSnapshot(
                state,
                detail,
                failures,
                processId,
                bundleVersion,
                nextRetry);
            return snapshot;
        }

        private void PublishTransition(
            CompanionLaunchSnapshot value)
        {
            emit(
                "companion-launch-state",
                new Dictionary<string, string>(
                    StringComparer.Ordinal)
                {
                    ["bundleVersion"] = value.BundleVersion,
                    ["detail"] = value.Detail,
                    ["nextRetryUtc"] =
                        value.NextRetryUtc?.ToString(
                            "O",
                            CultureInfo.InvariantCulture)
                        ?? string.Empty,
                    ["ownedProcessId"] =
                        value.OwnedProcessId?.ToString(
                            CultureInfo.InvariantCulture)
                        ?? string.Empty,
                    ["recentFailureCount"] =
                        value.RecentFailureCount.ToString(
                            CultureInfo.InvariantCulture),
                    ["state"] = value.State.ToString()
                });
            try
            {
                StatusChanged?.Invoke(value);
            }
            catch (Exception exception)
            {
                logWarning(
                    "T12 launch status observer fault: "
                    + exception.Message);
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(
                    nameof(CompanionLauncher));
            }
        }

        private sealed class AttemptResult
        {
            private AttemptResult(
                bool ready,
                bool terminal,
                string detail,
                string bundleVersion)
            {
                Ready = ready;
                Terminal = terminal;
                Detail = detail;
                BundleVersion = bundleVersion;
            }

            public bool Ready { get; }
            public bool Terminal { get; }
            public string Detail { get; }
            public string BundleVersion { get; }

            public static AttemptResult Succeeded(
                string bundleVersion)
            {
                return new AttemptResult(
                    true,
                    false,
                    string.Empty,
                    bundleVersion);
            }

            public static AttemptResult Failed(
                string detail,
                string bundleVersion = "")
            {
                return new AttemptResult(
                    false,
                    false,
                    detail,
                    bundleVersion);
            }

            public static AttemptResult TerminalFailure(
                string detail)
            {
                return new AttemptResult(
                    false,
                    true,
                    detail,
                    string.Empty);
            }
        }
    }
}
