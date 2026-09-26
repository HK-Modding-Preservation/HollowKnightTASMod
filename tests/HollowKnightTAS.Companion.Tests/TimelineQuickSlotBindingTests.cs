using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HollowKnightTAS.Companion.Automation;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Companion.ViewModels;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests;

[TestClass]
public sealed class TimelineQuickSlotBindingTests
{
    [TestMethod]
    public void BindingExistingAncestorPersistsSelectedBranchWithoutCreatingNodes()
    {
        var path = Path.Combine(Path.GetTempPath(), "timeline-bind-" + Guid.NewGuid().ToString("N"), "timelines.json");
        var store = new StudioTimelineStore(path);
        var movie = new MovieV2Document("test", new MovieV2Header("game", "api", "mod",
            MovieProtocolV2.NativeProfileId, MovieProtocolV2.ActionSchemaId, false, new string('a', 64), 800, 450),
            new[] { new NativeFrameRun(30, Array.Empty<GameInputSample>(), new MovieSourceSpan("test", 1, 1, 1), 50, true) });
        var codec = new MovieV2Codec();
        store.Update(lib =>
        {
            var tree = lib.Trees[0];
            tree.Add(5, codec.WriteCanonical(movie), new System.Collections.Generic.Dictionary<string, string>());
            tree.Add(20, codec.WriteCanonical(movie), new System.Collections.Generic.Dictionary<string, string>());
            tree.Add(20, codec.WriteCanonical(MovieV2RangeEditor.Paint(movie, 10, 1, "Left", true)), new System.Collections.Generic.Dictionary<string, string>());
        });
        using var sessions = new SessionRegistry("timeline-binding-test");
        using var broker = new AutomationBroker(sessions);
        var vm = new MainViewModel(sessions, new MovieEditorService(), new CapabilityBroker(),
            new NativeHostLauncher(AppContext.BaseDirectory), broker);
        Assert.IsFalse(vm.BindTimelineQuickSlotCommand.CanExecute(null));
        typeof(MainViewModel).GetField("worldlines", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, store);
        vm.SelectedTimelineTree = store.Library.Trees[0];
        vm.SelectedWorldline = vm.WorldlineLeaves.First();
        var leafId = vm.SelectedWorldline.Id;
        vm.SelectedTimelineNode = vm.WorldlinePath.Single(n => n.Frame == 5);
        var nodeId = vm.SelectedTimelineNode.Id;
        vm.SelectedQuickSlot = 3;
        var count = store.Library.Trees[0].Nodes.Count;
        var text = vm.MovieText;
        Assert.IsTrue(vm.BindTimelineQuickSlotCommand.CanExecute(null));
        vm.BindTimelineQuickSlotCommand.Execute(null);
        var reloaded = new StudioTimelineStore(path);
        Assert.AreEqual($"{vm.SelectedTimelineTree.Id}:{nodeId}:{leafId}", reloaded.Library.QuickSlots[3]);
        Assert.AreEqual(count, reloaded.Library.Trees[0].Nodes.Count);
        Assert.AreEqual(text, vm.MovieText);
        StringAssert.Contains(vm.QuickSlotLabels[3], "F4");
        vm.SelectedWorldline = vm.WorldlineLeaves.Last();
        vm.SelectedTimelineNode = vm.WorldlinePath.Single(n => n.Frame == 5);
        vm.BindTimelineQuickSlotCommand.Execute(null);
        Assert.AreNotEqual(reloaded.Library.QuickSlots[3], new StudioTimelineStore(path).Library.QuickSlots[3]);
        Assert.AreEqual(count, store.Library.Trees[0].Nodes.Count);
    }
}
