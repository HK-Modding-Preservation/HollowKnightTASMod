using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class StudioTimelineTreeTests
    {
        private static MovieV2Document Movie() => new("test", new MovieV2Header("game", "api", "mod",
            MovieProtocolV2.NativeProfileId, MovieProtocolV2.ActionSchemaId, false, new string('a', 64), 800, 450),
            new[] { new NativeFrameRun(500, Array.Empty<GameInputSample>(), new MovieSourceSpan("test", 1, 1, 1), 50, true) });
        private static string Text(MovieV2Document movie) => new MovieV2Codec().WriteCanonical(movie);
        private static Dictionary<string, string> Baseline() => new() { ["user1.dat"] = "original" };

        [TestMethod]
        public void EditedHistoryBranchesAtLatestMatchingSaveForBothEditingWorkflows()
        {
            foreach (var truncateAtSave in new[] { false, true })
            {
                var tree = new TimelineTree(); var original = Movie();
                tree.Add(10, Text(truncateAtSave ? MovieV2Prefix.Take(original, 10) : original), Baseline());
                tree.Add(30, Text(truncateAtSave ? MovieV2Prefix.Take(original, 30) : original), Baseline());
                var edited = MovieV2RangeEditor.Paint(original, 15, 3, "Left", true);
                tree.Add(20, Text(truncateAtSave ? MovieV2Prefix.Take(edited, 20) : edited), Baseline());
                CollectionAssert.AreEqual(new[] { 0, 1, 2 }, tree.PathTo(2).Select(n => n.Id).ToArray());
                CollectionAssert.AreEqual(new[] { 0, 1, 3 }, tree.PathTo(3).Select(n => n.Id).ToArray());
                CollectionAssert.AreEqual(new[] { 2, 3 }, tree.Leaves.Select(n => n.Id).ToArray());
                Assert.IsTrue(MovieV2Prefix.Matches(TimelineTree.Parse(tree.Nodes[1].Movie), edited, 10));
            }
        }
        [TestMethod]
        public void SameFrameSnapshotsDoNotOverwriteAndFutureEditsDoNotForkPast()
        {
            var tree = new TimelineTree(); var movie = Movie();
            tree.Add(10, Text(movie), Baseline());
            var future = MovieV2RangeEditor.Paint(movie, 10, 1, "Left", true);
            tree.Add(10, Text(future), Baseline());
            Assert.AreEqual(1, tree.Nodes[2].ParentId);
            var past = MovieV2RangeEditor.SetFrameRate(movie, 9, 1, 120);
            tree.Add(10, Text(past), Baseline());
            Assert.AreEqual(0, tree.Nodes[3].ParentId);
            Assert.AreEqual(Text(movie), tree.Nodes[1].Movie);
        }
        [TestMethod]
        public void SubtreeDeletionPreservesSiblingAndDoesNotRecycleNodeIds()
        {
            var tree = new TimelineTree(); var movie = Movie();
            tree.Add(10, Text(movie), Baseline()); tree.Add(30, Text(movie), Baseline());
            var edited = MovieV2RangeEditor.Paint(movie, 15, 1, "Left", true);
            tree.Add(20, Text(edited), Baseline()); tree.Add(40, Text(edited), Baseline());
            tree.Delete(3);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, tree.Nodes.Select(n => n.Id).ToArray());
            Assert.AreEqual(5, tree.Add(20, Text(edited), Baseline()).Id);
            tree.Delete(0);
            Assert.AreEqual(1, tree.Nodes.Count); Assert.AreEqual(0, tree.Nodes[0].Id);
        }
        [TestMethod]
        public void PersistReloadAndFailedTransactionKeepOriginalTreeIntact()
        {
            var directory = Path.Combine(Path.GetTempPath(), "hktas-timeline-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "timelines.json");
            try
            {
                var store = new StudioTimelineStore(path);
                store.Update(l => l.Trees[0].Add(10, Text(Movie()), Baseline()));
                var saved = File.ReadAllText(path);
                Assert.ThrowsExactly<InvalidOperationException>(() => store.Update(l =>
                    l.Trees[0].Add(20, Text(Movie()), new Dictionary<string, string> { ["user1.dat"] = "changed" })));
                Assert.AreEqual(saved, File.ReadAllText(path));
                Assert.AreEqual(2, store.Library.Trees[0].Nodes.Count);
                var reloaded = new StudioTimelineStore(path);
                Assert.AreEqual(1, reloaded.Library.Trees[0].Leaves.Single().Id);
                Assert.AreEqual(10L, reloaded.Library.Trees[0].Nodes[1].Frame);
                store.Update(l => l.Trees[0].Delete(1));
                Assert.AreEqual(1, new StudioTimelineStore(path).Library.Trees[0].Nodes.Count);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
        [TestMethod]
        public void FrameZeroSaveResolvesUnknownBootstrapHeaderWithoutLosingAncestry()
        {
            var tree = new TimelineTree();
            var unknown = new MovieV2Document("test", new MovieV2Header("unknown", "unknown", "unknown",
                MovieProtocolV2.NativeProfileId, MovieProtocolV2.ActionSchemaId, false, "none", 0, 0), Movie().Runs);
            tree.Add(0, Text(unknown), Baseline());
            var next = tree.Add(10, Text(Movie()), Baseline());
            Assert.AreEqual(1, next.ParentId);
        }
    }
}
