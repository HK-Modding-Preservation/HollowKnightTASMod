using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Companion.Automation;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Companion.ViewModels;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests;

[TestClass]
public sealed class SaveWithoutSeekTests
{
    private static string Movie() => new MovieV2Codec().WriteCanonical(new MovieV2Document("test",
        new MovieV2Header("game", "api", "mod", MovieProtocolV2.NativeProfileId,
            MovieProtocolV2.ActionSchemaId, false, new string('a', 64), 800, 450),
        new[] { new NativeFrameRun(500, Array.Empty<GameInputSample>(), new MovieSourceSpan("test", 1, 1, 1), 50, true) }));

    [TestMethod]
    [DataRow("Replay")]
    [DataRow("Recording")]
    public async Task SavingFutureFromFrameZeroDoesNotIssueNativeCommandOrRestart(string mode)
    {
        var root = Path.Combine(Path.GetTempPath(), "hktas-save-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var sessions = new SessionRegistry("save-target-test");
            using var boot = new StartupBootController();
            var coordinator = new FullRunMovieCoordinator(boot);
            using var broker = new AutomationBroker(sessions, fullRunMovies: coordinator,
                automationDirectory: Path.Combine(root, "automation"));
            var restarts = 0;
            var vm = new MainViewModel(sessions, new MovieEditorService(), new CapabilityBroker(),
                new NativeHostLauncher(AppContext.BaseDirectory), broker, startupBoot: boot,
                fullRunMovies: coordinator, restartProtectedGame: () => { restarts++; return Task.CompletedTask; });
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var gate = boot.BeginV2();
            typeof(FullRunMovieCoordinator).GetField("gate", flags)!.SetValue(coordinator, gate);
            typeof(FullRunMovieCoordinator).GetField("mode", flags)!.SetValue(coordinator, mode);
            typeof(FullRunMovieCoordinator).GetField("saves", flags)!.SetValue(coordinator,
                ProtectedSaveSession.Prepare("save-test", Path.Combine(root, "original"), Path.Combine(root, "shadow")));
            using var mapping = MemoryMappedFile.OpenExisting("Local\\HKTAS.Boot." + gate.Token + ".V2State");
            using var view = mapping.CreateViewAccessor();
            using var ready = EventWaitHandle.OpenExisting("Local\\HKTAS.Boot." + gate.Token + ".Ready");
            view.Write(92, 1); view.Write(76, 0); view.Write(40, 0L); ready.Set(); boot.Refresh();
            var store = new StudioTimelineStore(Path.Combine(root, "timelines.json"));
            typeof(MainViewModel).GetField("worldlines", flags)!.SetValue(vm, store);
            typeof(MainViewModel).GetField("activeDraftTree", flags)!.SetValue(vm, store.Library.Trees[0].Id);
            vm.MovieText = Movie(); vm.RefreshGridCommand.Execute(null);
            var sequence = view.ReadInt64(48);
            await vm.FrameMenuAsync("save", 300).WaitAsync(TimeSpan.FromSeconds(3));
            StringAssert.Contains(vm.GridStatus, "游戏位置未改变");
            Assert.AreEqual(0, restarts);
            Assert.AreEqual(sequence, view.ReadInt64(48), "Saving must not submit Step/Run/Seek/Pause when already paused.");
            Assert.AreEqual(0L, boot.NativeCompletedFrames);
            Assert.AreEqual(Movie(), vm.MovieText);
            var tree = new StudioTimelineStore(Path.Combine(root, "timelines.json")).Library.Trees[0];
            Assert.AreEqual(0L, tree.Nodes.Single(n => n.IsBranchTip).Frame);
            var saved = tree.Nodes.Single(n => n.Frame == 300);
            Assert.IsTrue(saved.IsUnverified);
            Assert.AreEqual(Movie(), saved.Movie);
            Assert.AreEqual(saved.Id, vm.SelectedTimelineNode!.Id);
            Assert.AreEqual(0L, tree.MatchingSavedFrame(Movie()));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void FutureBookmarkDoesNotAdvanceTipAndBecomesVerifiedOnlyAfterMatchingRun()
    {
        var tree = new TimelineTree();
        var hashes = new Dictionary<string, string>();
        var tip = tree.UpdateTip(100, Movie(), hashes, null);
        var future = tree.Add(300, Movie(), hashes, unverified: true);
        tree.UpdateTip(50, Movie(), hashes, tip.Id);
        Assert.AreEqual(100L, tip.Frame);
        Assert.IsTrue(future.IsUnverified);
        Assert.AreEqual(0L, tree.MatchingSavedFrame(Movie()));
        Assert.IsFalse(tree.PathTo(tip.Id).Contains(future));
        var past = tree.Add(20, Movie(), hashes);
        tree.UpdateTip(50, Movie(), hashes, tip.Id);
        Assert.AreEqual(100L, tip.Frame);
        Assert.IsTrue(tree.PathTo(tip.Id).Contains(past));
        tree.UpdateTip(300, Movie(), hashes, tip.Id);
        Assert.IsFalse(future.IsUnverified);
        Assert.IsTrue(tree.PathTo(tip.Id).Contains(future));
        Assert.AreEqual(300L, tip.Frame);
    }
}
