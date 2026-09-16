using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Core.Ipc;

namespace HollowKnightTAS.Companion.Services
{
    // Called only after the shared control surface grants exclusive authority.
    // Every request is pinned to a RuntimeSessionClient, never a current-window
    // selection or a bootstrap file that could change during the operation.
    internal sealed class RuntimeRecordingRestartHost : IRecordingRestartHost
    {
        private readonly RuntimeSessionClient source;
        private readonly SessionRegistry sessions;
        private readonly IColdRestoreLaunchEnvironmentResolver resolver;

        public RuntimeRecordingRestartHost(RuntimeSessionClient source, SessionRegistry sessions,
            IColdRestoreLaunchEnvironmentResolver resolver)
        { this.source = source; this.sessions = sessions; this.resolver = resolver; }

        public async Task<IPreparedRecordingRestart> PrepareAsync(int slot, CancellationToken token)
        {
            if (slot < 1 || slot > 4) throw new ArgumentOutOfRangeException(nameof(slot));
            if (!source.IsConnected || !sessions.Sessions.Contains(source))
                throw new InvalidOperationException("Source Runtime is disconnected.");
            var slotFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", "Team Cherry", "Hollow Knight", "user" + slot + ".dat");
            if (!File.Exists(slotFile)) throw new FileNotFoundException("Selected existing game slot is unavailable.", slotFile);
            var environment = resolver.Resolve(source);
            if (environment.Launcher is not VerifiedGameLauncher launcher)
                throw new InvalidOperationException("Verified interactive launcher is unavailable.");
            var attestation = await AttestAsync(source, token);
            environment.SourceLaunchVerifier.RequireTrusted(source, attestation);
            var state = await StateAsync(source, token);
            if (state["playbackMode"] != "Idle" ||
                (state["controlMode"] != "Running" && state["controlMode"] != "Paused"))
                throw new InvalidOperationException("Finish active playback or stepping before restarting.");
            return new Prepared(source, sessions, launcher);
        }

        private sealed class Prepared : IPreparedRecordingRestart
        {
            private readonly RuntimeSessionClient source;
            private readonly SessionRegistry sessions;
            private readonly VerifiedGameLauncher launcher;
            public Prepared(RuntimeSessionClient source, SessionRegistry sessions, VerifiedGameLauncher launcher)
            { this.source = source; this.sessions = sessions; this.launcher = launcher; }
            public async Task ExitSourceAsync(CancellationToken token)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var state = await StateAsync(source, timeout.Token);
                if (state["controlMode"] == "Running")
                    await RequestAsync(source, IpcMessageTypes.Pause, null, null, timeout.Token);
                do
                {
                    state = await StateAsync(source, timeout.Token);
                    if (state["controlMode"] == "Paused") break;
                    await Task.Delay(50, timeout.Token);
                } while (state["controlMode"] == "Pausing");
                if (state["controlMode"] != "Paused") throw new InvalidOperationException("Source failed to pause.");
                // Runtime rejects pending saves/restores/batches; no retry.
                await RequestAsync(source, IpcMessageTypes.QuitGame, null, null, timeout.Token);
                await new ExactColdRestoreProcessMonitor().WaitForExitAsync(source.GameProcessId,
                    new DateTimeOffset(source.GameProcessStartTimeUtcTicks, TimeSpan.Zero),
                    TimeSpan.FromSeconds(30), token);
            }
            public async Task<IRecordingRestartTarget> LaunchAsync(string operationId, CancellationToken token)
            {
                var runId = "interactive-" + operationId;
                var handle = await launcher.LaunchInteractiveAsync(runId, TimeSpan.FromSeconds(60), token);
                return new Target(sessions, handle, runId);
            }
            public void Dispose() { }
        }

        private sealed class Target : IRecordingRestartTarget
        {
            private readonly SessionRegistry sessions;
            private readonly VerifiedGameLaunchHandle handle;
            private readonly string runId;
            public Target(SessionRegistry sessions, VerifiedGameLaunchHandle handle, string runId)
            { this.sessions = sessions; this.handle = handle; this.runId = runId; }
            public async Task WaitForRecordingOriginAsync(int slot, CancellationToken token)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(120));
                RuntimeSessionClient? target;
                do
                {
                    if (handle.HasExited) throw new InvalidOperationException("Target exited before Runtime attachment.");
                    target = sessions.Sessions.SingleOrDefault(s => s.IsConnected && s.GameProcessId == handle.ProcessId
                        && s.GameProcessStartTimeUtcTicks == handle.ProcessStartedAtUtc.UtcTicks);
                    if (target == null) await Task.Delay(100, timeout.Token);
                } while (target == null);
                var attestation = await AttestAsync(target, timeout.Token);
                if (attestation.RunId != runId) throw new InvalidDataException("Target startup run binding mismatch.");
                var loaded = false;
                while (true)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    if (handle.HasExited) throw new InvalidOperationException("Target exited before recording origin was ready.");
                    var state = await StateAsync(target, timeout.Token);
                    if (state["recordingOriginStatus"] == "Faulted")
                        throw new InvalidOperationException(state["recordingOriginDetail"]);
                    if (!loaded && state.TryGetValue("isStableTitleMenu", out var menu) && menu == "true")
                    {
                        loaded = true;
                        await RequestAsync(target, IpcMessageTypes.LoadGameSlot,
                            new Dictionary<string, string> { ["slot"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture) }, null, timeout.Token);
                    }
                    if (loaded && state["recordingOriginStatus"] == "Ready")
                    {
                        await RequestAsync(target, IpcMessageTypes.Pause, null, null, timeout.Token);
                        do
                        {
                            state = await StateAsync(target, timeout.Token);
                            if (state["controlMode"] == "Paused") return;
                            await Task.Delay(50, timeout.Token);
                        } while (state["controlMode"] == "Pausing");
                        throw new InvalidOperationException("Target failed to pause at the recording origin.");
                    }
                    await Task.Delay(100, timeout.Token);
                }
            }
            public void ReleaseSupervision() => handle.ReleaseSupervision();
            public void Dispose() => handle.Dispose();
        }

        private static Task<IReadOnlyDictionary<string, string>> StateAsync(RuntimeSessionClient session, CancellationToken token)
            => RequestAsync(session, IpcMessageTypes.RequestSnapshot,
                new Dictionary<string, string> { ["statusOnly"] = "true" }, IpcMessageTypes.RuntimeStatus, token);

        private static async Task<StartupProfileAttestation> AttestAsync(RuntimeSessionClient session, CancellationToken token)
        {
            var fields = await RequestAsync(session, IpcMessageTypes.RequestStartupProfileAttestation,
                null, IpcMessageTypes.StartupProfileAttestation, token);
            if (!StartupProfileAttestation.TryParse(fields, out var result, out var error) || result == null)
                throw new InvalidDataException(error);
            if (result.Status != StartupProfileAttestationStatus.Verified || result.Profile != StartupProfileContract.ProfileId
                || result.ProcessId != session.GameProcessId || result.ProcessStartTimeUtcTicks != session.GameProcessStartTimeUtcTicks)
                throw new InvalidDataException("Runtime startup identity is not verified.");
            return result;
        }

        private static async Task<IReadOnlyDictionary<string, string>> RequestAsync(RuntimeSessionClient session,
            string message, Dictionary<string, string>? fields, string? responseType, CancellationToken token)
        {
            var id = "restart-request-" + Guid.NewGuid().ToString("N");
            fields ??= new Dictionary<string, string>();
            fields["requestId"] = id;
            var accepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var response = new TaskCompletionSource<IReadOnlyDictionary<string, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Received(object? sender, IpcEnvelope envelope)
            {
                var decoded = IpcPayloadCodec.TryDeserialize(envelope.PayloadUtf8);
                if (!decoded.Success || decoded.Fields == null || !decoded.Fields.TryGetValue("requestId", out var request) || request != id) return;
                if (envelope.MessageType == IpcMessageTypes.CommandRejected)
                    accepted.TrySetException(new InvalidOperationException(decoded.Fields.TryGetValue("detail", out var detail) ? detail : "Runtime rejected restart operation."));
                else if (envelope.MessageType == IpcMessageTypes.CommandAccepted)
                { accepted.TrySetResult(true); if (responseType == null) response.TrySetResult(decoded.Fields); }
                else if (envelope.MessageType == responseType) response.TrySetResult(decoded.Fields);
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            session.EnvelopeReceived += Received;
            try
            {
                await session.SendCommandAsync(message, fields, timeout.Token);
                await accepted.Task.WaitAsync(timeout.Token);
                return await response.Task.WaitAsync(timeout.Token);
            }
            finally { session.EnvelopeReceived -= Received; }
        }
    }
}
