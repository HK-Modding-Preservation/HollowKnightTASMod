using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Companion.ViewModels;
using HollowKnightTAS.Core.Movie;

internal static partial class StudioScenarioHarness
{
    static async Task RunInfoOverlayAsync(string[] args)
    {
        var advanced = args.Contains("--info-custom");
        var expressions = advanced ? new[] { "hero.dashCooldownTimer", "player.geo", "position.x", "game.gameState",
            "component(\"/Knight\", \"HeroController\").jump_steps", "fsm(\"/Knight\", \"Spell Control\", \"MP Cost\")", "hero.noSuchField" } : Array.Empty<string>();
        var vm = (MainViewModel)app.MainWindow.DataContext;
        var boot = Field<StartupBootController>(app, "startupBoot");
        var movies = Field<FullRunMovieCoordinator>(app, "fullRunMovies");
        typeof(MainViewModel).GetField("activeSequenceDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, output);
        var controller = Field<InfoOverlayController>(vm, "infoController");
        async Task<IReadOnlyDictionary<string, JsonElement>> Snapshot(string label)
        {
            var before = boot.NativeCompletedFrames;
            var request = typeof(MainViewModel).GetMethod("RequestRuntimeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var fields = new Dictionary<string, string> { ["requestId"] = "info-test-" + Guid.NewGuid().ToString("N"), ["view"] = "info",
                ["watches"] = JsonSerializer.Serialize(expressions) };
            var task = (Task<IReadOnlyDictionary<string, string>>)request.Invoke(vm, new object[] { vm.SelectedSession!.Client, fields, CancellationToken.None })!;
            var result = await task;
            await File.WriteAllTextAsync(Path.Combine(output, label + ".json"), result["snapshotJson"]);
            boot.Refresh(); Require(boot.NativeCompletedFrames == before && boot.IsWaiting, label + " observation does not advance paused native frame");
            return InfoOverlayModel.Decode(result["snapshotJson"]);
        }
        await Field<Func<string, Task>>(vm, "launchGame")(@"D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight.exe");
        Execute(vm.NewFullRunMovieCommand);
        await Command(vm.StepCommand);
        await Until(() => vm.SelectedSession?.Client.IsConnected == true, "info Runtime connected", 120);
        await vm.PollInputGridProgressAsync();
        var observed = await VideoRuntimeMovie(vm, movies);
        var title = await Snapshot("title");
        Require(!title.ContainsKey("x"), "title has no stale hero coordinates");
        var baselinePath = args.Single(a => a.StartsWith("--info-baseline=", StringComparison.Ordinal)).Split('=', 2)[1];
        var baseline = SequencePackage.Read(baselinePath);
        var source = TimelineTree.Parse(baseline.Movie);
        var candidate = new MovieV2Document("info-display-candidate", TimelineTree.Parse(observed.Movie).Header,
            source.Runs.Select(r => new NativeFrameRun(r.RepeatCount, r.Samples, r.Span, r.FramesPerSecond, true, r.RngSeed)));
        var candidatePath = Path.Combine(output, "info-candidate.hktaspack");
        await SequencePackage.WriteAsync(candidatePath, new MovieV2Codec().WriteCanonical(candidate), baseline.InitialSaves!);
        await vm.OpenMovieFileAsync(candidatePath);
        await vm.FrameMenuAsync("rebuild", 1500);
        AtFrame(vm, boot, 1500, "gameplay reached under new Runtime identity");
        var values = await Snapshot("gameplay-1500");
        Require(values.ContainsKey("x") && values.ContainsKey("dash") && values.ContainsKey("shade"), "hero coordinates and both cooldowns present");
        await Until(() => Field<InfoOverlayWindow?>(controller, "window")?.IsVisible == true, "overlay visibly attached to game");
        var overlay = Field<InfoOverlayWindow>(controller, "window");
        if (advanced)
        {
            Require(vm.InfoSettings.Anchor == "右上" && vm.InfoSettings.MarginX == 8 && vm.InfoSettings.MarginY == 8, "default panel uses top-right corner");
            Require(values["watch:position.x"].GetDouble() == values["x"].GetDouble(), "custom vector component matches preset coordinate");
            Require(values["watch:hero.dashCooldownTimer"].ValueKind == JsonValueKind.Number, "custom private field available");
            Require(values["watch:player.geo"].ValueKind == JsonValueKind.Number, "custom PlayerData field available");
            Require(values["watch:game.gameState"].GetString() == "PLAYING", "custom GameManager enum available");
            Require(values["watch:component(\"/Knight\", \"HeroController\").jump_steps"].ValueKind == JsonValueKind.Number, "explicit component query available");
            Require(values["watch:fsm(\"/Knight\", \"Spell Control\", \"MP Cost\")"].GetInt32() == 33, "FSM variable read from backing fields");
            Require(values["watch:hero.noSuchField"].ValueKind == JsonValueKind.Null, "one invalid field does not fail other rows");
            vm.AddInfoItem(InfoOverlayModel.Fields.Single(f => f.Id == "custom"));
            vm.InfoSettings.Items.Last().Expression = "player.geo";
            vm.InfoSettings.Items.Last().Label = "Geo";
            vm.InfoSettings.Items.Last().Precision = 0;
            await Task.Delay(600);
            Require(InfoOverlaySettings.Load(Path.Combine(output, "info-settings.json")).Items.Last().Expression == "player.geo", "custom expression persists through real VM");
            await MoveInfoGameWhileObservationsWait(vm, boot, overlay);
        }
        var pausedNative = boot.NativeCompletedFrames;
        await Task.Delay(550);
        boot.Refresh(); Require(boot.NativeCompletedFrames == pausedNative, "automatic overlay polling leaves game paused");
        var canvas = (Canvas)overlay.Content; canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(canvas.ActualWidth), (int)Math.Ceiling(canvas.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(output, "live-overlay.png"))) encoder.Save(stream);
        vm.InfoSettings.Enabled = false;
        await Until(() => !overlay.IsVisible, "toggle hides overlay");
        vm.InfoSettings.Enabled = true;
        await Until(() => overlay.IsVisible, "toggle restores overlay");
        await Command(vm.StepCommand); await vm.PollInputGridProgressAsync();
        var stepped = await Snapshot("gameplay-1501");
        Require(stepped["frame"].GetInt64() == 1501, "step publishes next Movie frame");
        vm.InfoSettings.Items[0].Label = "Test frame"; vm.InfoSettings.Items.Move(0, 2);
        vm.InfoSettings.Anchor = "右下"; vm.InfoSettings.FontSize = 18;
        await Task.Delay(600);
        var stored = InfoOverlaySettings.Load(Path.Combine(output, "info-settings.json"));
        Require(stored.Items[2].Label == "Test frame" && stored.Anchor == "右下", "settings and order persist through real VM");
        await vm.FrameMenuAsync("seek", 1520);
        AtFrame(vm, boot, 1520, "playback advances with overlay enabled");
        await Snapshot("gameplay-1520");
        await vm.FrameMenuAsync("rebuild", 1500);
        AtFrame(vm, boot, 1500, "cold restore reconnects overlay");
        var restored = await Snapshot("restored-1500");
        foreach (var key in new[] { "room", "x", "y", "dash", "shade", "health", "soul" })
            Require(restored[key].ToString() == values[key].ToString(), "restored display matches " + key);
        await Until(() => overlay.IsVisible, "overlay visible after restore");
        movies.VerifyOriginalSavesUnchanged();
        Require(boot.FullRunFaultCode == 0, "no native fault after display controls");
    }

    static async Task MoveInfoGameWhileObservationsWait(MainViewModel vm, StartupBootController boot, InfoOverlayWindow overlay)
    {
        var game = Field<System.Diagnostics.Process>(app, "startupGame"); game.Refresh(); var hwnd = game.MainWindowHandle;
        var overlayHandle = new System.Windows.Interop.WindowInteropHelper(overlay).Handle;
        GetInfoWindowRect(hwnd, out var before);
        var nativeFrame = boot.NativeCompletedFrames;
        // Exercise Windows' actual modal move loop while the Runtime cannot service observations.
        PostInfoMessage(hwnd, 0x0112, new IntPtr(0xF010), IntPtr.Zero); // WM_SYSCOMMAND / SC_MOVE
        try
        {
            PostInfoMessage(hwnd, 0x0100, new IntPtr(0x27), IntPtr.Zero); // select move direction
            await Until(() => overlay.IsOwnerMoving, "native window entered modal move loop", 5);
            await Task.Delay(2300); // longer than the observation timeout
            for (var i = 0; i < 4; i++)
            {
                PostInfoMessage(hwnd, 0x0100, new IntPtr(0x27), IntPtr.Zero);
                PostInfoMessage(hwnd, 0x0101, new IntPtr(0x27), IntPtr.Zero);
                await Task.Delay(100);
                GetInfoClientRect(hwnd, out var client); var origin = new InfoPoint(); InfoClientToScreen(hwnd, ref origin);
                GetInfoWindowRect(overlayHandle, out var shown);
                Require(overlay.IsVisible && shown.Left == origin.X && shown.Top == origin.Y
                    && shown.Right - shown.Left == client.Right && shown.Bottom - shown.Top == client.Bottom,
                    "overlay remains aligned before releasing move " + i);
            }
            GetInfoWindowRect(hwnd, out var moved);
            Require(moved.Left != before.Left || moved.Top != before.Top, "game changed position during modal move");
            boot.Refresh(); Require(boot.NativeCompletedFrames == nativeFrame, "moving window did not advance native game frame");
        }
        finally { PostInfoMessage(hwnd, 0x0100, new IntPtr(0x0D), IntPtr.Zero); PostInfoMessage(hwnd, 0x0101, new IntPtr(0x0D), IntPtr.Zero); }
        await Until(() => !overlay.IsOwnerMoving, "native move loop finished", 5);
    }
    [StructLayout(LayoutKind.Sequential)] struct InfoRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct InfoPoint { public int X, Y; }
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] static extern bool PostInfoMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")] static extern bool GetInfoWindowRect(IntPtr window, out InfoRect bounds);
    [DllImport("user32.dll", EntryPoint = "GetClientRect")] static extern bool GetInfoClientRect(IntPtr window, out InfoRect bounds);
    [DllImport("user32.dll", EntryPoint = "ClientToScreen")] static extern bool InfoClientToScreen(IntPtr window, ref InfoPoint point);
}
