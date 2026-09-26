using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using HollowKnightTAS.Companion;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Companion.ViewModels;

// Opt-in integration harness: the real App/VM/IPC/verified launcher, no UIA client.
// Run with --headless --scenario-output=<absolute-directory> only with no game/Studio running.
internal static class StudioScenarioHarness
{
    static string output = "";
    static App app = null!;
    static int finished;
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    static void Log(string text) { File.AppendAllText(Path.Combine(output, "scenario.log"), DateTime.Now.ToString("O") + " " + text + "\n"); }
    static void Require(bool value, string detail) { if (!value) throw new Exception(detail); Log("PASS " + detail); }
    static void AtFrame(MainViewModel vm, StartupBootController boot, long frame, string detail)
    {
        Log($"BOUNDARY movie={Field<long>(vm, "currentFullRunMovieFrame")} native={boot.NativeCompletedFrames} waiting={boot.IsWaiting} fault={boot.FullRunFaultCode} status={vm.GridStatus} appStatus={vm.Status}");
        Require(boot.IsWaiting && boot.FullRunFaultCode == 0 && Field<long>(vm, "currentFullRunMovieFrame") == frame, detail);
    }
    static async Task Until(Func<bool> ready, string detail, int seconds = 25)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!ready()) { if (DateTime.UtcNow > deadline) throw new TimeoutException(detail); await Task.Delay(100); }
    }
    static async Task Command(ICommand command)
    {
        Require(command.CanExecute(null), "command enabled");
        await Field<Func<Task>>(command, "execute")();
    }
    [STAThread]
    static int Main(string[] args)
    {
        output = args.Single(a => a.StartsWith("--scenario-output=")).Split('=', 2)[1];
        Directory.CreateDirectory(output);
        if (!args.Contains("--headless") || Process.GetProcessesByName("hollow_knight").Length != 0
            || Process.GetProcessesByName("HollowKnightTAS.Companion").Length != 0) return 12;
        app = new App(); app.InitializeComponent();
        var guard = new Thread(Guard) { IsBackground = true }; guard.Start();
        app.Startup += async (_, _) =>
        {
            try
            {
                await Until(() => app.MainWindow?.DataContext is MainViewModel, "app ready");
                if (args.Contains("--reopen-only")) await Reopen();
                else await Run();
                Log("ALL SCENARIOS PASSED");
                // Exercise the actual async Closing save handler and App-owned game exit.
                app.MainWindow.Close();
            }
            catch (Exception ex) { Log("FAIL " + ex); Interlocked.Exchange(ref finished, 1); app.Shutdown(1); }
        };
        var code = app.Run();
        Interlocked.Exchange(ref finished, 1);
        Log("APP EXIT code=" + code);
        return code;
    }
    static async Task Reopen()
    {
        var vm = (MainViewModel)app.MainWindow.DataContext;
        var store = new StudioTimelineStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HollowKnightTAS", "studio-frame-saves", "timelines.json"));
        var tree = store.Library.Trees.Single(t => t.Id == store.Library.ActiveTreeId);
        var leaf = tree.Leaves.Where(n => n.IsBranchTip).OrderByDescending(n => n.Frame).First();
        vm.InitializeWorldlines();
        vm.SelectedTimelineTreeId = tree.Id;
        await Until(() => vm.IsInputGridInteractive, "reopened tree selected");
        vm.SelectedWorldlineId = leaf.Id;
        await Until(() => vm.IsInputGridInteractive, "reopened leaf selected");
        Require(vm.MovieText == leaf.Movie && vm.SelectedWorldline!.Frame == leaf.Frame,
            "reopened Studio loads persisted complete leaf " + leaf.Frame);
        Require(Process.GetProcessesByName("hollow_knight").Length == 0, "reopening draft does not launch game");
    }
    static async Task Run()
    {
        var vm = (MainViewModel)app.MainWindow.DataContext;
        var boot = Field<StartupBootController>(app, "startupBoot");
        var movies = Field<FullRunMovieCoordinator>(app, "fullRunMovies");
        var launch = Field<Func<string, Task>>(vm, "launchGame");
        await launch(@"D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight.exe");
        Require(boot.IsWaiting && boot.NativeCompletedFrames == 0, "protected native zero");
        vm.NewFullRunMovieCommand.Execute(null);
        await Command(vm.StepCommand);
        await Until(() => vm.SelectedSession != null, "runtime connected");
        await vm.PollInputGridProgressAsync();
        await vm.FrameMenuAsync("seek", 120);
        Require(vm.GridStatus.Contains("120"), "forward seek 120: " + vm.GridStatus);
        await vm.SaveCurrentBranchAsync();
        var treeId = vm.SelectedTimelineTreeId!;
        Require(vm.SelectedTimelineTree!.Leaves.Any(n => n.IsBranchTip && n.Frame == 120), "actual furthest tip 120");
        await vm.FrameMenuAsync("save", -1);
        var explicitNode = vm.SelectedTimelineNode!.Id;
        Require(!vm.SelectedTimelineNode.IsBranchTip, "explicit save remains independent");
        var follows = 0;
        vm.InputGridPositionChanged += _ => follows++;
        var oldPid = Field<Process>(app, "startupGame").Id;
        await Command(vm.TogglePauseCommand);
        var before = follows;
        await Until(() => follows >= before + 3, "live follow while running", 8);
        Require(!boot.IsWaiting, "follow before pause");
        await Command(vm.TogglePauseCommand);
        await vm.PollInputGridProgressAsync();
        var furthest = vm.SelectedTimelineTree!.Leaves.Max(n => n.Frame);
        Require(furthest > 120, "tip progressed to " + furthest);
        var completeMovie = vm.MovieText;
        await File.WriteAllTextAsync(Path.Combine(output, "recorded.hktas"), completeMovie);
        var frozenMoves = 0;
        vm.InputGridPositionChanged += _ => { if (vm.IsRestorePresentationFrozen) frozenMoves++; };
        var sawFrozen = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.IsRestorePresentationFrozen) && vm.IsRestorePresentationFrozen) sawFrozen = true; };
        await vm.FrameMenuAsync("rebuild", 120);
        AtFrame(vm, boot, 120, "rebuild to 120");
        Require(sawFrozen && frozenMoves == 0, "restore freezes following");
        Require(Field<Process>(app, "startupGame").Id != oldPid, "rebuild restarted owned game");
        await vm.SaveCurrentBranchAsync();
        Require(vm.SelectedTimelineTree!.Leaves.Any(n => n.IsBranchTip && n.Frame == furthest), "rewind preserves furthest");
        // Same-current target previously called forward-only seek and failed.
        vm.GridStart = "120";
        await Command(vm.ApplyGridAndSeekCommand);
        AtFrame(vm, boot, 120, "recompute CURRENT frame 120");
        // Edit a past FPS range: it must create an unexecuted branch, then execute exactly.
        vm.GridStart = "10"; vm.GridCount = "3"; vm.SelectedFrameRate = "100";
        vm.SetFrameRateCommand.Execute(null);
        Require(vm.InputRows[10].FramesPerSecond == 100 && vm.InputRows[13].FramesPerSecond == 50, "selection FPS boundaries");
        await vm.SaveCurrentBranchAsync();
        Require(vm.SelectedTimelineTree!.Leaves.Count() >= 2, "past edit forks branch");
        var altered = vm.MovieText;
        await File.WriteAllTextAsync(Path.Combine(output, "altered.hktas"), altered);
        await vm.FrameMenuAsync("rebuild", 50);
        AtFrame(vm, boot, 50, "edited branch executes to 50");
        await vm.SaveCurrentBranchAsync();
        // Mid-session file switch then restore behind the old native/game boundary.
        await vm.OpenMovieFileAsync(Path.Combine(output, "recorded.hktas"));
        Require(vm.MovieText == completeMovie, "file switch draft preserved");
        await vm.FrameMenuAsync("restore", 20);
        AtFrame(vm, boot, 20, "switched file restores to 20");
        await vm.SaveCurrentBranchAsync();
        // Select an old leaf, which replaces the complete draft without Apply Branch.
        vm.SelectedTimelineTreeId = treeId;
        await Until(() => vm.IsInputGridInteractive, "worldline selected");
        var oldTree = vm.SelectedTimelineTree!;
        var leaf = oldTree.Leaves.First(n => n.IsBranchTip && n.Frame == furthest);
        vm.SelectedWorldlineId = leaf.Id;
        await Until(() => vm.IsInputGridInteractive, "leaf selected");
        Require(vm.MovieText == leaf.Movie, "leaf selection loads complete movie");
        vm.SelectTimelineNode(explicitNode);
        await Command(vm.RestoreTimelineNodeCommand);
        Require(boot.IsWaiting && boot.FullRunFaultCode == 0 && vm.TimelineTreeStatus.Contains("120"), "explicit ancestor restored");
        await vm.SaveCurrentBranchAsync(closing: true);
        var reopened = new StudioTimelineStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HollowKnightTAS", "studio-frame-saves", "timelines.json"));
        Require(reopened.Library.Trees.Single(t => t.Id == treeId).Leaves.Any(n => n.Frame == furthest), "disk reload keeps furthest");
        movies.VerifyOriginalSavesUnchanged();
        Log("native=" + boot.NativeCompletedFrames + " fault=" + boot.FullRunFaultCode + " followEvents=" + follows);
    }
    [StructLayout(LayoutKind.Sequential)] struct Memory
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended;
    }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref Memory memory);
    static void Guard()
    {
        var deadline = DateTime.UtcNow.AddMinutes(4);
        while (Volatile.Read(ref finished) == 0)
        {
            try
            {
                using var self = Process.GetCurrentProcess(); self.Refresh();
                var memory = new Memory { Length = (uint)Marshal.SizeOf<Memory>() };
                if (!GlobalMemoryStatusEx(ref memory)) throw new Exception("memory monitor unavailable");
                var game = Field<Process?>(app, "startupGame");
                long gameBytes = 0;
                if (game != null && !game.HasExited) { game.Refresh(); gameBytes = game.PrivateMemorySize64; }
                Log($"RESOURCE host={self.PrivateMemorySize64} game={gameBytes} available={memory.AvailablePhysical} commitAvailable={memory.AvailablePage}");
                if (DateTime.UtcNow > deadline || self.PrivateMemorySize64 > 768L * 1024 * 1024
                    || gameBytes > 3L * 1024 * 1024 * 1024 || memory.AvailablePhysical < 2UL * 1024 * 1024 * 1024
                    || memory.AvailablePage < 2UL * 1024 * 1024 * 1024)
                {
                    Log("FAIL watchdog resource/deadline boundary");
                    if (game != null && !game.HasExited) game.Kill();
                    Environment.Exit(15);
                }
            }
            catch (Exception ex) { Log("GUARD " + ex.Message); }
            Thread.Sleep(1000);
        }
    }
}
