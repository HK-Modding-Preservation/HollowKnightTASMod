using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using HollowKnightTAS.Core.Automation;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Ipc;
using HollowKnightTAS.Runtime.Ipc;

namespace HollowKnightTAS.Runtime.Companion
{
    /// <summary>Created only by a title-menu click. No journal, input, timing or save hooks.</summary>
    internal sealed class ManualStartupService : IDisposable
    {
        private readonly RuntimeCommandQueue commands = new RuntimeCommandQueue(64);
        private readonly StartupHandoffGuard guard = new StartupHandoffGuard();
        private readonly NamedPipeRuntimeServer server;
        private readonly CompanionLauncher launcher;
        private readonly CompanionSessionRegistration registration;
        private readonly Action<string> failed;
        private readonly Action<string> log;
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private double? exitAt;
        private bool cancelled;
        private bool started;
        private bool disposed;

        public ManualStartupService(Action<string> log, Action<string> warn, Action<string> error,
            Action<string> failed)
        {
            this.failed = failed;
            this.log = log;
            var session = StartupActivationPolicy.ManualSessionPrefix + Guid.NewGuid().ToString("N");
            var token = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(token);
            var runtimePath = typeof(HollowKnightTASMod).Assembly.Location;
            using (var process = Process.GetCurrentProcess())
                registration = new CompanionSessionRegistration(session, process.Id,
                    process.StartTime.ToUniversalTime().Ticks,
                    Sha256Utility.ComputeUtf8Hex("manual-startup-only-v1"),
                    Sha256Utility.ComputeFileHex(runtimePath),
                    Sha256Utility.ComputeFileHex(typeof(CompanionSessionRegistration).Assembly.Location),
                    "HollowKnightTAS.Runtime." + session, CompanionProtocolMetadata.SupportedProtocols,
                    false, token, AutomationMode.Disabled);
            server = new NamedPipeRuntimeServer(session, registration.GameProcessId, registration.PipeName,
                token, commands, 128, log, warn, error);
            launcher = new CompanionLauncher(Path.GetDirectoryName(runtimePath)!, registration, server,
                TimeSpan.FromSeconds(10), log, warn, (name, _) => log(name));
        }

        public static bool IsStableTitle()
        {
            var manager = GameManager.instance;
            return manager != null && !manager.IsInSceneTransition
                && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Menu_Title"
                && HeroController.SilentInstance == null;
        }

        public void Start()
        {
            if (started) return;
            started = true;
            On.GameManager.Update += Update;
            server.Start();
            launcher.ManualStartOrReconnect();
            log("Manual Studio startup requested at title.");
        }

        private void Update(On.GameManager.orig_Update original, GameManager self)
        {
            original(self);
            if (disposed) return;
            var launch = launcher.Snapshot;
            if (launch.State == CompanionLaunchState.Unavailable
                || launch.State == CompanionLaunchState.Rejected
                || launch.State == CompanionLaunchState.VersionConflict
                || launch.State == CompanionLaunchState.CircuitOpen)
            {
                failed("Manual startup failed: " + launch.Detail);
                return;
            }
            // Returning to title before clicking is allowed. Leaving it after clicking revokes this request.
            if (!IsStableTitle())
            {
                failed("Manual startup cancelled because the game left the stable title.");
                return;
            }
            if (exitAt.HasValue)
            {
                if (elapsed.Elapsed.TotalSeconds >= exitAt.Value) ExitProcess(0);
                return;
            }
            for (var count = 0; count < 16 && commands.TryDequeue(out var command); count++)
            {
                command.Fields.TryGetValue("requestId", out var requestId);
                var accepted = true;
                string detail;
                try { detail = Handle(command); }
                catch (Exception error) { accepted = false; detail = error.Message; }
                var published = server.TryPublish(accepted ? IpcMessageTypes.CommandAccepted : IpcMessageTypes.CommandRejected,
                    new Dictionary<string, string>
                    {
                        ["requestId"] = requestId ?? string.Empty,
                        ["command"] = command.MessageType,
                        ["detail"] = detail,
                        ["errorCode"] = accepted ? string.Empty : "ManualStartupOnly"
                    });
                if (exitAt.HasValue)
                {
                    if (!published) { exitAt = null; cancelled = true; }
                    break;
                }
                if (cancelled) break;
            }
            if (cancelled || elapsed.Elapsed > TimeSpan.FromSeconds(60))
                failed("Manual startup did not complete; original game retained. Retry from the menu.");
        }

        private string Handle(ValidatedRuntimeCommand command)
        {
            if (command.MessageType != IpcMessageTypes.StartupHandoff)
                throw new InvalidOperationException("This session only permits the requested title restart.");
            var fields = command.Fields;
            if (fields["processId"] != registration.GameProcessId.ToString(CultureInfo.InvariantCulture)
                || fields["processStartTimeUtcTicks"] != registration.GameProcessStartTimeUtcTicks.ToString(CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Manual startup process identity mismatch.");
            switch (fields["phase"])
            {
                case "cancel": cancelled = true; return "cancelled";
                case "prepare": return guard.Prepare(fields["operationId"], false, IsStableTitle());
                case "commit":
                    guard.Commit(fields["operationId"], false, IsStableTitle());
                    // Give the pipe writer time to deliver the acknowledgement before the known-safe title exit.
                    exitAt = elapsed.Elapsed.TotalSeconds + 0.2;
                    log("Manual startup committed; exiting title process for controlled launch.");
                    return "startup-handoff-exiting";
                default: throw new InvalidOperationException("Unknown manual startup phase.");
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (started) On.GameManager.Update -= Update;
            if (server.IsConnected)
            {
                try
                {
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
                        launcher.RequestSessionShutdownAsync(false, timeout.Token).GetAwaiter().GetResult();
                }
                catch (Exception error) { log("Manual startup session cleanup: " + error.Message); }
            }
            launcher.Dispose();
            server.Dispose();
            commands.Clear();
        }

        [DllImport("kernel32.dll")]
        private static extern void ExitProcess(uint exitCode);
    }
}
