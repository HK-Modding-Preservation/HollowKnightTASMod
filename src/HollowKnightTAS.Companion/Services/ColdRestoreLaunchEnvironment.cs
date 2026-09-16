using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.ReplaySave;

namespace HollowKnightTAS.Companion.Services
{
    public interface IColdRestoreGameLaunchHandle : IDisposable
    {
        int ProcessId { get; }
        DateTimeOffset ProcessStartedAtUtc { get; }
        string LauncherEvidence { get; }
        bool HasExited { get; }
        void ReleaseSupervision();
    }

    public interface IColdRestoreGameLauncher
    {
        Task<IColdRestoreGameLaunchHandle> LaunchAsync(
            ColdRestoreIntent intent,
            TimeSpan timeout,
            CancellationToken cancellationToken);
    }

    public interface IColdRestoreLaunchEnvironmentResolver
    {
        ColdRestoreLaunchEnvironment Resolve(
            RuntimeSessionClient sourceSession);
    }

    public interface IColdRestoreProcessMonitor
    {
        Task WaitForExitAsync(
            int processId,
            DateTimeOffset processStartedAtUtc,
            TimeSpan timeout,
            CancellationToken cancellationToken);
    }

    public sealed class ExactColdRestoreProcessMonitor :
        IColdRestoreProcessMonitor
    {
        public async Task WaitForExitAsync(
            int processId,
            DateTimeOffset processStartedAtUtc,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Process process;
            try
            {
                process = Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                return;
            }

            using (process)
            {
                process.Refresh();
                if (new DateTimeOffset(process.StartTime.ToUniversalTime())
                        .UtcTicks
                    != processStartedAtUtc.UtcTicks)
                {
                    throw new InvalidOperationException(
                        "Source Hollow Knight PID was reused before exit confirmation.");
                }

                using (var linked = CancellationTokenSource
                           .CreateLinkedTokenSource(cancellationToken))
                {
                    linked.CancelAfter(timeout);
                    await process.WaitForExitAsync(linked.Token);
                }
            }
        }
    }

    public sealed class ColdRestoreLaunchEnvironment
    {
        public ColdRestoreLaunchEnvironment(
            string gameExecutableSha256,
            string unityPlayerSha256,
            string assemblyCSharpSha256,
            string companionAssemblySha256,
            string startupProfileSha256,
            string observerAssemblySha256,
            ITrustedSourceLaunchVerifier sourceLaunchVerifier,
            IColdRestoreGameLauncher launcher)
        {
            GameExecutableSha256 = RequireSha256(
                gameExecutableSha256,
                nameof(gameExecutableSha256));
            UnityPlayerSha256 = RequireSha256(
                unityPlayerSha256,
                nameof(unityPlayerSha256));
            AssemblyCSharpSha256 = RequireSha256(
                assemblyCSharpSha256,
                nameof(assemblyCSharpSha256));
            CompanionAssemblySha256 = RequireSha256(
                companionAssemblySha256,
                nameof(companionAssemblySha256));
            StartupProfileSha256 = RequireSha256(
                startupProfileSha256,
                nameof(startupProfileSha256));
            ObserverAssemblySha256 = string.IsNullOrEmpty(
                observerAssemblySha256)
                ? string.Empty
                : RequireSha256(
                    observerAssemblySha256,
                    nameof(observerAssemblySha256));
            SourceLaunchVerifier = sourceLaunchVerifier
                                   ?? throw new ArgumentNullException(
                                       nameof(sourceLaunchVerifier));
            Launcher = launcher
                       ?? throw new ArgumentNullException(nameof(launcher));
        }

        public string GameExecutableSha256 { get; }
        public string UnityPlayerSha256 { get; }
        public string AssemblyCSharpSha256 { get; }
        public string CompanionAssemblySha256 { get; }
        public string StartupProfileSha256 { get; }
        public string ObserverAssemblySha256 { get; }
        public ITrustedSourceLaunchVerifier SourceLaunchVerifier { get; }
        public IColdRestoreGameLauncher Launcher { get; }

        public ColdRestoreBuildFingerprint BuildFingerprint(
            RuntimeSessionClient session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            return new ColdRestoreBuildFingerprint(
                session.EnvironmentManifestSha256,
                GameExecutableSha256,
                UnityPlayerSha256,
                AssemblyCSharpSha256,
                session.RuntimeAssemblySha256,
                CompanionAssemblySha256,
                StartupProfileSha256,
                ObserverAssemblySha256);
        }

        private static string RequireSha256(string value, string name)
        {
            if (!MovieProtocolV1.IsLowerSha256(value))
            {
                throw new ArgumentException(
                    "A canonical lowercase SHA-256 is required.",
                    name);
            }

            return value;
        }
    }

    public sealed class PackagedColdRestoreLaunchEnvironmentResolver :
        IColdRestoreLaunchEnvironmentResolver
    {
        private readonly string startupBundleRoot;
        private readonly string companionAssemblyPath;
        private readonly string observerAssemblyPath;
        private readonly VerifiedLaunchReceiptStore launchReceiptStore;

        public PackagedColdRestoreLaunchEnvironmentResolver(
            string startupBundleRoot,
            string companionAssemblyPath,
            VerifiedLaunchReceiptStore launchReceiptStore,
            string observerAssemblyPath = "")
        {
            if (string.IsNullOrWhiteSpace(startupBundleRoot))
            {
                throw new ArgumentException(
                    "A packaged ClockStartup root is required.",
                    nameof(startupBundleRoot));
            }

            if (string.IsNullOrWhiteSpace(companionAssemblyPath))
            {
                throw new ArgumentException(
                    "The running Companion assembly path is required.",
                    nameof(companionAssemblyPath));
            }

            this.startupBundleRoot = Path.GetFullPath(startupBundleRoot);
            this.companionAssemblyPath = Path.GetFullPath(
                companionAssemblyPath);
            this.launchReceiptStore = launchReceiptStore
                                      ?? throw new ArgumentNullException(
                                          nameof(launchReceiptStore));
            this.observerAssemblyPath = string.IsNullOrWhiteSpace(
                observerAssemblyPath)
                ? string.Empty
                : Path.GetFullPath(observerAssemblyPath);
        }

        public ColdRestoreLaunchEnvironment Resolve(
            RuntimeSessionClient sourceSession)
        {
            if (sourceSession == null)
            {
                throw new ArgumentNullException(nameof(sourceSession));
            }

            if (!sourceSession.IsConnected)
            {
                throw new InvalidOperationException(
                    "The source Runtime session is not connected.");
            }

            string executable;
            using (var process = Process.GetProcessById(
                       sourceSession.GameProcessId))
            {
                process.Refresh();
                var startedAtUtc = new DateTimeOffset(
                    process.StartTime.ToUniversalTime());
                if (startedAtUtc.UtcTicks
                    != sourceSession.GameProcessStartTimeUtcTicks)
                {
                    throw new InvalidOperationException(
                        "The source Hollow Knight PID was reused.");
                }

                executable = process.MainModule?.FileName
                             ?? throw new InvalidOperationException(
                                 "The source Hollow Knight executable path is unavailable.");
            }

            var profile = VerifiedStartupProfile.Load(
                startupBundleRoot,
                executable);
            var observerSha256 = string.IsNullOrEmpty(observerAssemblyPath)
                ? string.Empty
                : Sha256Utility.ComputeFileHex(observerAssemblyPath);
            return new ColdRestoreLaunchEnvironment(
                profile.GameExecutableSha256,
                profile.UnityPlayerSha256,
                profile.AssemblyCSharpSha256,
                Sha256Utility.ComputeFileHex(companionAssemblyPath),
                profile.StartupProfileSha256,
                observerSha256,
                new VerifiedSourceLaunchReceiptVerifier(
                    launchReceiptStore,
                    profile),
                new VerifiedGameLauncher(profile, launchReceiptStore));
        }
    }
}
