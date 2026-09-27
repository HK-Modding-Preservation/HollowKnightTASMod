using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
        var vm = (MainViewModel)app.MainWindow.DataContext;
        var boot = Field<StartupBootController>(app, "startupBoot");
        var movies = Field<FullRunMovieCoordinator>(app, "fullRunMovies");
        typeof(MainViewModel).GetField("activeSequenceDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, output);
        var controller = Field<InfoOverlayController>(vm, "infoController");
        async Task<IReadOnlyDictionary<string, JsonElement>> Snapshot(string label)
        {
            var before = boot.NativeCompletedFrames;
            var request = typeof(MainViewModel).GetMethod("RequestRuntimeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var fields = new Dictionary<string, string> { ["requestId"] = "info-test-" + Guid.NewGuid().ToString("N"), ["view"] = "info" };
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
}
