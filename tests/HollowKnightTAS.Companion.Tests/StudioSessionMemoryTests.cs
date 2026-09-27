using System;
using System.Linq;
using System.Reflection;
using HollowKnightTAS.Companion.Automation;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Companion.ViewModels;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests;

[TestClass]
public sealed class StudioSessionMemoryTests
{
    [TestMethod]
    public void BulkInsertUsesIndependentCountAndUndoesAsOneEdit()
    {
        using var sessions = new SessionRegistry("bulk-insert-test");
        using var broker = new AutomationBroker(sessions);
        var vm = new MainViewModel(sessions, new MovieEditorService(), new CapabilityBroker(),
            new NativeHostLauncher(AppContext.BaseDirectory), broker);
        var movie = new MovieV2Document("test", new MovieV2Header("game", "api", "mod",
            MovieProtocolV2.NativeProfileId, MovieProtocolV2.ActionSchemaId, false, new string('a', 64), 800, 450),
            new[] { new NativeFrameRun(10, Array.Empty<GameInputSample>(), new MovieSourceSpan("test", 1, 1, 1), 50, true) });
        var original = new MovieV2Codec().WriteCanonical(movie);
        vm.MovieText = original;
        vm.RefreshGridCommand.Execute(null);
        vm.GridStart = "5";
        vm.GridCount = "非连续选区";
        vm.GridInsertCount = "12";
        vm.InsertGridCommand.Execute(null);
        Assert.AreEqual(22L, TimelineTree.Parse(vm.MovieText).Runs.Sum(run => run.RepeatCount));
        vm.UndoGridCommand.Execute(null);
        Assert.AreEqual(original, vm.MovieText);
        vm.RedoGridCommand.Execute(null);
        Assert.AreEqual(22L, TimelineTree.Parse(vm.MovieText).Runs.Sum(run => run.RepeatCount));
        var inserted = vm.MovieText;
        foreach (var invalid in new[] { "0", "-1", "abc", "99999999999999999999" })
        {
            vm.GridInsertCount = invalid;
            vm.InsertGridCommand.Execute(null);
            Assert.AreEqual(inserted, vm.MovieText);
        }
    }

    [TestMethod]
    public void ReopeningStudioClearsTimelineAndSlotsWhileRepeatedInitializationKeepsSession()
    {
        using var sessions = new SessionRegistry("session-memory-test");
        using var broker = new AutomationBroker(sessions);
        MainViewModel Open() => new(sessions, new MovieEditorService(), new CapabilityBroker(),
            new NativeHostLauncher(AppContext.BaseDirectory), broker);
        T Field<T>(object owner, string name) => (T)owner.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

        var first = Open();
        first.InitializeWorldlines();
        var store = Field<StudioTimelineStore>(first, "worldlines");
        Assert.IsNull(Field<string?>(store, "path"), "Studio must not load or write a timeline file.");
        store.Update(library =>
        {
            library.Trees.Add(new TimelineTree { Name = "Session checkpoint" });
            library.QuickSlots[0] = library.Trees[0].Id + ":0:0";
        });
        var slots = Field<StudioQuickSlots>(first, "quickSlots");
        Assert.IsNull(Field<string?>(slots, "path"));
        slots.Set(0, "session-save", 100, "pending-session");
        first.InitializeWorldlines();
        Assert.AreSame(store, Field<StudioTimelineStore>(first, "worldlines"));
        Assert.AreEqual(2, store.Library.Trees.Count);
        Assert.AreEqual(1, store.Library.QuickSlots.Count);

        var reopened = Open();
        reopened.InitializeWorldlines();
        var fresh = Field<StudioTimelineStore>(reopened, "worldlines");
        Assert.AreEqual(1, fresh.Library.Trees.Count);
        Assert.AreEqual(1, fresh.Library.Trees[0].Nodes.Count);
        Assert.AreEqual("", fresh.Library.Trees[0].Nodes[0].Movie);
        Assert.AreEqual(0, fresh.Library.QuickSlots.Count);
        Assert.AreEqual(1, reopened.TimelineTrees.Count);
        Assert.IsTrue(reopened.QuickSlotLabels.All(label => label.Contains("空时间线槽")));
        Assert.IsTrue(Field<StudioQuickSlots>(reopened, "quickSlots").Slots
            .All(slot => slot.SaveId.Length == 0 && slot.PendingLabel.Length == 0));
    }

    [TestMethod]
    public void FailedMemoryEditKeepsOriginalLibrary()
    {
        var store = new StudioTimelineStore();
        var original = store.Library;
        Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
            store.Update(library => library.Trees[0].Nodes.Clear()));
        Assert.AreSame(original, store.Library);
        Assert.AreEqual(1, store.Library.Trees[0].Nodes.Count);
    }
}
