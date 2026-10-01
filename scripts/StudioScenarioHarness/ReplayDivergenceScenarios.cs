using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using HollowKnightTAS.Companion.Automation;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Companion.ViewModels;
using HollowKnightTAS.Core.Automation;

internal static partial class StudioScenarioHarness
{
    static async Task RunReplayDivergenceAsync(string[] args)
    {
        var vm = (MainViewModel)app.MainWindow.DataContext;
        var boot = Field<StartupBootController>(app, "startupBoot");
        var movies = Field<FullRunMovieCoordinator>(app, "fullRunMovies");
        void Set(string name, object value) => typeof(MainViewModel).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, value);
        Set("worldlines", new StudioTimelineStore(Path.Combine(output, "timelines.json")));
        Set("initialSaveCacheRoot", Path.Combine(output, "initial-saves"));
        Set("activeSequenceDirectory", output);
        var source = args.Single(a => a.StartsWith("--divergence-sequence=")).Split('=', 2)[1];
        var copy = Path.Combine(output, "input.hktaspack");
        File.Copy(source, copy, false);
        await vm.OpenMovieFileAsync(copy);
        var originalMovie = vm.MovieText;
        await Field<Func<string, Task>>(vm, "launchGame")(@"D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight.exe");
        var observations = new Dictionary<string, Dictionary<string, string>>();

        async Task Capture(string label, bool screenshot = true)
        {
            await Until(() => vm.SelectedSession?.Client.IsConnected == true, "Runtime connected", 60);
            await vm.PollInputGridProgressAsync();
            var result = await Field<AutomationBroker>(vm, "automationBroker").ExecuteHumanAsync(
                AutomationCommandIds.FullRunStatus, AutomationScope.ObserveStatus,
                null, string.Empty, null, CancellationToken.None);
            Require(result.Success, label + " status: " + result.Detail);
            observations[label] = result.Data.Where(p => p.Key is "movieFrame" or "sceneName"
                or "heroX" or "heroY" or "heroHealth" or "saveSlot" or "frameBoundary"
                or "faultCode" or "mismatchCount").ToDictionary(p => p.Key, p => p.Value);
            await File.WriteAllTextAsync(Path.Combine(output, label + ".json"), JsonSerializer.Serialize(result.Data));
            var watchFields = new Dictionary<string, string> {
                ["requestId"] = Guid.NewGuid().ToString("N"), ["view"] = "info",
                ["watches"] = JsonSerializer.Serialize(new[] { "game.gameState", "game.entryGateName",
                    "hero.isEnteringFirstLevel", "hero.transitionState", "hero.cState.transitioning",
                    "component(\"/Sequence\", \"OpeningSequence\").isAsync",
                    "component(\"/Sequence\", \"OpeningSequence\").isLevelReady",
                    "component(\"/Sequence\", \"OpeningSequence\").chainSequence.currentSequenceIndex" }) };
            var request = typeof(MainViewModel).GetMethod("RequestRuntimeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var observation = await (Task<IReadOnlyDictionary<string, string>>)request.Invoke(vm,
                new object[] { vm.SelectedSession!.Client, watchFields, CancellationToken.None })!;
            await File.WriteAllTextAsync(Path.Combine(output, label + "-info.json"), observation["snapshotJson"]);
            if (screenshot && !args.Contains("--divergence-no-screenshots"))
            {
                var game = Field<Process>(app, "startupGame"); game.Refresh();
                var capture = typeof(RestorePresentation).Assembly.GetType("HollowKnightTAS.Companion.Services.GameWindowCapture")!;
                var image = await (Task<BitmapSource>)capture.GetMethod("CaptureAsync")!.Invoke(null, new object[] { game.MainWindowHandle })!;
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using (var stream = File.Create(Path.Combine(output, label + ".png"))) encoder.Save(stream);
            }
            movies.VerifyOriginalSavesUnchanged();
            Log(label + " movie=" + result.Data["movieFrame"] + " native=" + boot.NativeCompletedFrames + " shadow=" + movies.ShadowRoot);
        }

        var target = int.Parse(args.SingleOrDefault(a => a.StartsWith("--divergence-target="))?.Split('=', 2)[1] ?? "700");
        var start = int.Parse(args.SingleOrDefault(a => a.StartsWith("--divergence-start="))?.Split('=', 2)[1] ?? "100");
        // Step across the save-load transition, then compare independent cold restores.
        await Command(vm.StepCommand);
        if (!args.Contains("--divergence-restore-only"))
        {
            await vm.FrameMenuAsync("seek", start);
            AtFrame(vm, boot, start, "reach manual-step starting boundary");
            await Capture("step-" + start);
            for (var frame = start + 1; frame <= target; frame++)
            {
                await Command(vm.StepCommand);
                await Capture("step-" + frame, frame % 50 == 0 || frame is 114 or 640 or 641 || frame == target);
            }
        }
        var repeats = int.Parse(args.SingleOrDefault(a => a.StartsWith("--divergence-repeats="))?.Split('=', 2)[1] ?? "3");
        for (var i = 0; i < repeats; i++)
        {
            await vm.FrameMenuAsync("rebuild", target);
            AtFrame(vm, boot, target, "repeated restore to gameplay boundary");
            await Capture("restore-" + i + "-" + target);
            if (args.Contains("--divergence-pause-probe"))
            {
                var pausedFrame = boot.NativeCompletedFrames;
                await Task.Delay(2000);
                boot.Refresh();
                await File.WriteAllTextAsync(Path.Combine(output, "pause-boundary-" + i + ".json"),
                    JsonSerializer.Serialize(new { before = pausedFrame, after = boot.NativeCompletedFrames,
                        waiting = boot.IsWaiting, fault = boot.FullRunFaultCode, stepEnabled = vm.StepCommand.CanExecute(null) }));
                await Capture("after-pause-" + i + "-" + target, false);
            }
        }
        Require(vm.MovieText == originalMovie, "probe did not edit movie input");
        movies.VerifyOriginalSavesUnchanged();
        var targetStates = observations.Where(p => p.Value["movieFrame"] == target.ToString())
            .ToDictionary(p => p.Key, p => p.Value);
        var distinctStates = targetStates.Values.Select(v => JsonSerializer.Serialize(v.OrderBy(p => p.Key)))
            .Distinct().Count();
        await File.WriteAllTextAsync(Path.Combine(output, "comparison.json"),
            JsonSerializer.Serialize(new { target, distinctStates, observations = targetStates }));
        Log("DIAGNOSTIC comparison distinct target states=" + distinctStates
            + "; harness completion is not a determinism pass");
        if (args.Contains("--verify-first-level"))
        {
            Require(!observations.Values.Any(v => v["sceneName"] == "Opening_Sequence"
                && v["frameBoundary"] == "GameplayInput"), "first-level loading never appears as gameplay input");
            Require(targetStates.Count >= repeats + (args.Contains("--divergence-restore-only") ? 0 : 1),
                "all requested target observations captured");
            Require(distinctStates == 1, "step and repeated restore have identical target gameplay state");
            Require(targetStates.Values.All(v => v["sceneName"] == "Tutorial_01"
                && v["faultCode"] == "0" && v["mismatchCount"] == "0"),
                "all targets reached the first gameplay scene without input/native faults");
        }
    }
}
