using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Companion.Automation;
using HollowKnightTAS.Companion.ViewModels;

namespace HollowKnightTAS.Companion
{
    public partial class App : Application
    {
        private readonly CancellationTokenSource shutdown =
            new CancellationTokenSource();
        private SingleInstanceCoordinator? singleInstance;
        private SessionRegistry? sessions;
        private ControlPipeServer? controlServer;
        private ColdRestoreSupervisor? coldRestoreSupervisor;
        private AutomationBroker? automationBroker;
        private bool shutdownWhenColdRestoreStops;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var bootstrap =
                    await BootstrapRegistrationReader.TryReadAsync(
                        e.Args,
                        shutdown.Token);
                singleInstance = new SingleInstanceCoordinator();
                if (!singleInstance.IsPrimary)
                {
                    if (bootstrap != null)
                    {
                        var forwarded =
                            await singleInstance.ForwardRegistrationAsync(
                                bootstrap,
                                TimeSpan.FromSeconds(5),
                                shutdown.Token);
                        Shutdown(forwarded ? 0 : 3);
                    }
                    else
                    {
                        Shutdown(0);
                    }

                    return;
                }

                var coldRestoreRoot = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "HollowKnightTAS",
                    "cold-restore");
                var coldIdentity =
                    ColdRestoreSupervisorIdentity.LoadOrCreate(
                        Path.Combine(coldRestoreRoot, "identity"));
                sessions = new SessionRegistry(
                    coldIdentity.CompanionInstanceId);
                var coldStore = new ColdRestoreIntentStore(
                    Path.Combine(coldRestoreRoot, "store"),
                    coldIdentity.CompanionInstanceId,
                    coldIdentity.ClaimSecret);
                var launchReceiptStore = new VerifiedLaunchReceiptStore(
                    Path.Combine(coldRestoreRoot, "launch-receipts"),
                    coldIdentity.CompanionInstanceId,
                    coldIdentity.ClaimSecret);
                var recovery = coldStore.Recover();
                if (recovery.IncompleteTransactionIds.Count != 0
                    || recovery.CorruptOperationIds.Count != 0)
                {
                    Console.Error.WriteLine(
                        "ColdRestoreRecoveryAttention:transactions="
                        + recovery.IncompleteTransactionIds.Count
                        + ";corrupt="
                        + recovery.CorruptOperationIds.Count);
                }

                coldRestoreSupervisor = new ColdRestoreSupervisor(
                    sessions,
                    coldStore,
                    coldIdentity,
                    new PackagedColdRestoreLaunchEnvironmentResolver(
                        Path.Combine(
                            AppContext.BaseDirectory,
                            "ClockStartup"),
                        typeof(App).Assembly.Location,
                        launchReceiptStore));
                coldRestoreSupervisor.ActivityChanged +=
                    OnColdRestoreActivityChanged;
                controlServer = new ControlPipeServer(
                    singleInstance.ControlPipeName,
                    sessions,
                    () => coldRestoreSupervisor == null
                          || !coldRestoreSupervisor.IsActive);
                controlServer.ExitRequested +=
                    OnControlExitRequested;
                controlServer.Start();
                if (bootstrap != null)
                {
                    if (!await sessions.RegisterAsync(
                            bootstrap,
                            shutdown.Token))
                    {
                        Console.Error.WriteLine(
                            "BootstrapRegistrationFailed:"
                            + sessions.LastRegistrationError);
                        Shutdown(4);
                        return;
                    }
                }

                automationBroker = new AutomationBroker(
                    sessions,
                    coldRestoreSupervisor);

                var viewModel = new MainViewModel(
                    sessions,
                    new MovieEditorService(),
                    new CapabilityBroker(),
                    new NativeHostLauncher(),
                    automationBroker,
                    async gamePath =>
                    {
                        var existingGames = System.Diagnostics.Process.GetProcessesByName("hollow_knight");
                        try
                        {
                            if (existingGames.Length != 0)
                                throw new InvalidOperationException("请先在游戏内正常保存并退出，然后启动 TAS 游戏。");
                        }
                        finally
                        {
                            foreach (var process in existingGames) process.Dispose();
                        }
                        var profile = await Task.Run(() => VerifiedStartupProfile.Load(
                            Path.Combine(AppContext.BaseDirectory, "ClockStartup"), gamePath), shutdown.Token);
                        var launcher = new VerifiedGameLauncher(profile, launchReceiptStore);
                        using var handle = await launcher.LaunchInteractiveAsync(
                            "interactive-" + Guid.NewGuid().ToString("N"),
                            TimeSpan.FromSeconds(60), shutdown.Token);
                        handle.ReleaseSupervision();
                    });
                var window = new MainWindow
                {
                    DataContext = viewModel
                };
                MainWindow = window;
                window.Closed += OnMainWindowClosed;
                if (!e.Args.Any(
                        value => string.Equals(
                            value,
                            "--headless",
                            StringComparison.OrdinalIgnoreCase)))
                {
                    window.Show();
                }

                var exitSeconds = ReadPositiveIntArgument(
                    e.Args,
                    "--exit-after-seconds=");
                if (exitSeconds > 0)
                {
                    _ = ExitAfterAsync(exitSeconds);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "HollowKnightTAS Companion failed to start.\n\n"
                    + exception.Message,
                    "HollowKnightTAS Companion",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(2);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            shutdown.Cancel();
            if (controlServer != null)
            {
                controlServer.ExitRequested -=
                    OnControlExitRequested;
            }
            if (coldRestoreSupervisor != null)
            {
                coldRestoreSupervisor.ActivityChanged -=
                    OnColdRestoreActivityChanged;
            }
            controlServer?.Dispose();
            automationBroker?.Dispose();
            coldRestoreSupervisor?.Dispose();
            sessions?.Dispose();
            singleInstance?.Dispose();
            shutdown.Dispose();
            base.OnExit(e);
        }

        private void OnControlExitRequested(
            object? sender,
            EventArgs eventArgs)
        {
            Dispatcher.BeginInvoke(
                new Action(() => Shutdown(0)));
        }

        private void OnMainWindowClosed(
            object? sender,
            EventArgs eventArgs)
        {
            if (sender is Window window)
            {
                window.Closed -= OnMainWindowClosed;
            }

            if (coldRestoreSupervisor?.IsActive == true)
            {
                shutdownWhenColdRestoreStops = true;
                return;
            }

            Shutdown(0);
        }

        private void OnColdRestoreActivityChanged(
            object? sender,
            EventArgs eventArgs)
        {
            if (!shutdownWhenColdRestoreStops
                || coldRestoreSupervisor?.IsActive == true)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() => Shutdown(0)));
        }

        private async Task ExitAfterAsync(int seconds)
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(seconds),
                    shutdown.Token);
                Dispatcher.Invoke(
                    () =>
                    {
                        if (coldRestoreSupervisor?.IsActive == true)
                        {
                            shutdownWhenColdRestoreStops = true;
                        }
                        else
                        {
                            Shutdown(0);
                        }
                    });
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static int ReadPositiveIntArgument(
            string[] arguments,
            string prefix)
        {
            var value = arguments.FirstOrDefault(
                argument => argument.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase));
            return value != null
                   && int.TryParse(
                       value.Substring(prefix.Length),
                       out var parsed)
                   && parsed > 0
                ? parsed
                : 0;
        }
    }
}
