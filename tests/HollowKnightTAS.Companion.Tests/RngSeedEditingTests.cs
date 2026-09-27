using System;
using System.Collections.Generic;
using System.Linq;
using HollowKnightTAS.Companion.Automation;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Companion.ViewModels;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class RngSeedEditingTests
    {
        private static MovieV2Document Movie() => new("rng", new MovieV2Header("game", "api", "mod",
            MovieProtocolV2.NativeProfileId, MovieProtocolV2.ActionSchemaId, false, new string('a', 64), 800, 450),
            new[] { new NativeFrameRun(20, Array.Empty<GameInputSample>(), new MovieSourceSpan("rng", 1, 1, 1)) });
        private static string Text(MovieV2Document movie) => new MovieV2Codec().WriteCanonical(movie);

        [TestMethod]
        public void SeedEditsDisplayUndoRedoCopyPasteAndRejectInvalidValues()
        {
            using var sessions = new SessionRegistry("rng-edit-test");
            using var broker = new AutomationBroker(sessions);
            var vm = new MainViewModel(sessions, new MovieEditorService(), new CapabilityBroker(),
                new NativeHostLauncher(AppContext.BaseDirectory), broker);
            vm.MovieText = Text(Movie()); vm.RefreshGridCommand.Execute(null);
            var original = vm.MovieText;
            Assert.IsTrue(vm.TrySetGridRngSeed(4, "0"));
            Assert.AreEqual(0, vm.InputRows[4].RngSeed);
            Assert.IsNull(vm.InputRows[3].RngSeed);
            Assert.IsNull(vm.InputRows[5].RngSeed);
            var seeded = vm.MovieText;
            Assert.IsTrue(TimelineTree.Parse(seeded).Runs.Skip(1).All(r => r.Authored));
            Assert.IsFalse(vm.TrySetGridRngSeed(4, "2147483648"));
            Assert.IsFalse(vm.TrySetGridRngSeed(4, "1.5"));
            Assert.AreEqual(seeded, vm.MovieText);
            vm.UndoGridCommand.Execute(null); Assert.AreEqual(original, vm.MovieText);
            vm.RedoGridCommand.Execute(null); Assert.AreEqual(seeded, vm.MovieText);
            vm.PaintGrid(4, 4, "Attack", true);
            Assert.AreEqual(0, vm.InputRows[4].RngSeed);
            Assert.IsTrue(vm.InputRows[4].Attack);
            vm.SelectedFrameRate = "120"; vm.SetFrameRateCommand.Execute(null);
            Assert.AreEqual(0, vm.InputRows[4].RngSeed);
            Assert.AreEqual(120, vm.InputRows[4].FramesPerSecond);
            // Exercise the production copy slicer without requiring the OS clipboard.
            var slice = (MovieV2Document)typeof(MainViewModel).GetMethod("SliceV2",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(null, new object[] { TimelineTree.Parse(vm.MovieText), 4L, 1L })!;
            var pasted = new MovieV2TimelineEditor().InsertFrames(TimelineTree.Parse(vm.MovieText), 8, slice.Runs);
            Assert.IsTrue(pasted.Success);
            Assert.AreEqual(0, new VirtualInputRows(pasted.Movie, 0)[8].RngSeed);
            Assert.IsTrue(vm.TrySetGridRngSeed(4, ""));
            Assert.IsNull(vm.InputRows[4].RngSeed);
            Assert.IsTrue(vm.InputRows[4].Attack);
            vm.UndoGridCommand.Execute(null); Assert.AreEqual(0, vm.InputRows[4].RngSeed);
        }

        [TestMethod]
        public void SeededWorldlineBranchesAtExecutedPrefixAndKeepsOriginal()
        {
            var tree = new TimelineTree();
            var baseline = new Dictionary<string, string> { ["user1.dat"] = "original" };
            var original = Text(Movie());
            var checkpoint = tree.Add(4, original, baseline);
            var first = tree.UpdateTip(10, original, baseline, null);
            var seeded = Text(MovieV2RangeEditor.SetRngSeed(Movie(), 4, 123));
            var second = tree.UpdateTip(tree.MatchingSavedFrame(seeded), seeded, baseline, first.Id);
            Assert.AreEqual(checkpoint.Id, second.ParentId);
            Assert.AreEqual(4L, second.Frame);
            Assert.AreEqual(original, first.Movie);
            Assert.AreEqual(10L, first.Frame);
            Assert.AreEqual(2, tree.Leaves.Count());
            Assert.IsTrue(MovieV2Prefix.Matches(TimelineTree.Parse(first.Movie), TimelineTree.Parse(second.Movie), 4));
            Assert.IsFalse(MovieV2Prefix.Matches(TimelineTree.Parse(first.Movie), TimelineTree.Parse(second.Movie), 5));
        }
    }
}
