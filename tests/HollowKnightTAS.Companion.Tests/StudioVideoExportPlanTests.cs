using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class StudioVideoExportPlanTests
    {
        private static MovieV2Document Movie(long frames = 100, string? environment = null)
            => new("test", new MovieV2Header("game", "api", "mod", MovieProtocolV2.NativeProfileId,
                MovieProtocolV2.ActionSchemaId, false, environment ?? new string('a', 64), 0, 0),
                frames == 0 ? Array.Empty<NativeFrameRun>() : new[] {
                    new NativeFrameRun(frames, Array.Empty<GameInputSample>(), new MovieSourceSpan("test", 1, 1, 1)) });
        private static string Text(MovieV2Document movie) => new MovieV2Codec().WriteCanonical(movie);
        private static TimelineTree Tree()
        {
            var tree = new TimelineTree();
            var baseline = new Dictionary<string, string> { ["user1.dat"] = "original" };
            tree.Add(10, Text(Movie()), baseline);
            tree.Add(30, Text(Movie()), baseline);
            return tree;
        }

        [TestMethod]
        public void WholeMovieUsesCanonicalCompleteInputAndAllFrames()
        {
            var text = Text(Movie());
            var plan = StudioVideoExportPlan.ForMovie(text.Replace("\n", "\r\n"));
            Assert.AreEqual(text, plan.Movie);
            Assert.AreEqual(0L, plan.StartMovieFrame);
            Assert.AreEqual(100L, plan.EndMovieFrame);
            Assert.IsNull(plan.TreeId);
            Assert.IsNull(plan.OriginalHashes);
        }

        [TestMethod]
        public void InvalidEmptyLegacyAndOversizedMoviesAreRejected()
        {
            foreach (var source in new[] { "", "not a movie", "{\"format\":\"hktas\",\"version\":1}\n", Text(Movie(0)),
                Text(Movie(1)).Replace("\"repeatCount\":1", "\"repeatCount\":10000001") })
                Assert.ThrowsExactly<InvalidDataException>(() => StudioVideoExportPlan.ForMovie(source));
        }

        [TestMethod]
        public void VariableFrameRateIsAcceptedIncludingFutureDraft()
        {
            var changed = Text(MovieV2RangeEditor.SetFrameRate(Movie(), 60, 1, 100));
            Assert.AreEqual(changed, StudioVideoExportPlan.ForMovie(changed).Movie);
            var tree = Tree();
            tree.Nodes.Single(node => node.Id == 2).Movie = changed;
            Assert.AreEqual(changed, StudioVideoExportPlan.ForTimeline(tree, 1, 2).Movie);
        }

        [TestMethod]
        public void ReverseSelectionFindsAncestryAndKeepsDescendantsFutureDraftOutsideCapture()
        {
            var tree = Tree();
            var future = MovieV2RangeEditor.Paint(Movie(), 50, 1, "Left", true);
            tree.Nodes.Single(node => node.Id == 2).Movie = Text(future);
            var forward = StudioVideoExportPlan.ForTimeline(tree, 1, 2);
            var reverse = StudioVideoExportPlan.ForTimeline(tree, 2, 1);
            Assert.AreEqual(10L, reverse.StartMovieFrame);
            Assert.AreEqual(30L, reverse.EndMovieFrame);
            Assert.AreEqual(Text(future), reverse.Movie);
            Assert.AreEqual(forward.Movie, reverse.Movie);
            Assert.AreEqual(100L, TimelineTree.Parse(reverse.Movie).Runs.Sum(run => run.RepeatCount));
        }

        [TestMethod]
        public void RootCanStartAClipAndUnverifiedEndpointIsReplayedNormally()
        {
            var tree = Tree();
            tree.Nodes[2].IsUnverified = true;
            var plan = StudioVideoExportPlan.ForTimeline(tree, 2, 0);
            Assert.AreEqual(0L, plan.StartMovieFrame);
            Assert.AreEqual(30L, plan.EndMovieFrame);
            Assert.IsTrue(tree.Nodes[2].IsUnverified);
        }

        [TestMethod]
        public void SiblingBranchesAndIdenticalOrEqualFrameEndpointsAreRejected()
        {
            var tree = Tree();
            var branch = tree.Add(40, Text(MovieV2RangeEditor.Paint(Movie(), 15, 1, "Left", true)), tree.OriginalHashes);
            Assert.ThrowsExactly<InvalidDataException>(() => StudioVideoExportPlan.ForTimeline(tree, 2, branch.Id));
            Assert.ThrowsExactly<InvalidDataException>(() => StudioVideoExportPlan.ForTimeline(tree, 1, 1));
            var equal = tree.Add(10, Text(Movie()), tree.OriginalHashes);
            Assert.ThrowsExactly<InvalidDataException>(() => StudioVideoExportPlan.ForTimeline(tree, 1, equal.Id));
        }

        [TestMethod]
        public void MissingDuplicateAndBrokenParentNodesAreRejected()
        {
            Assert.ThrowsExactly<InvalidDataException>(() => StudioVideoExportPlan.ForTimeline(Tree(), 1, 99));
            var duplicate = Tree(); duplicate.Nodes.Add(new TimelineNode { Id = 1, ParentId = 0 });
            Assert.ThrowsExactly<InvalidDataException>(() => StudioVideoExportPlan.ForTimeline(duplicate, 1, 2));
            var missing = Tree(); missing.Nodes[2].ParentId = 99;
            Assert.ThrowsExactly<InvalidDataException>(() => StudioVideoExportPlan.ForTimeline(missing, 1, 2));
            var detached = Tree(); detached.Nodes[1].ParentId = null;
            Assert.ThrowsExactly<InvalidDataException>(() => StudioVideoExportPlan.ForTimeline(detached, 1, 2));
        }

        [TestMethod]
        public void CyclesAndInvalidRootAreRejectedWithoutLooping()
        {
            var cycle = Tree(); cycle.Nodes[1].ParentId = 2; cycle.Nodes[1].Frame = 30;
            Assert.ThrowsExactly<InvalidDataException>(() => StudioVideoExportPlan.ForTimeline(cycle, 1, 2));
            var root = Tree(); root.Nodes[0].ParentId = 2;
            Assert.ThrowsExactly<InvalidDataException>(() => StudioVideoExportPlan.ForTimeline(root, 1, 2));
            var self = Tree(); self.Nodes[2].ParentId = 2;
            Assert.ThrowsExactly<InvalidDataException>(() => StudioVideoExportPlan.ForTimeline(self, 1, 2));
        }

        [TestMethod]
        public void PastInputAndHeaderConflictsAreRejected()
        {
            var input = Tree();
            input.Nodes[2].Movie = Text(MovieV2RangeEditor.Paint(Movie(), 5, 1, "Left", true));
            Assert.ThrowsExactly<InvalidDataException>(() => StudioVideoExportPlan.ForTimeline(input, 1, 2));
            var header = Tree(); header.Nodes[2].Movie = Text(Movie(environment: new string('b', 64)));
            Assert.ThrowsExactly<InvalidDataException>(() => StudioVideoExportPlan.ForTimeline(header, 1, 2));
            var root = Tree(); root.Nodes[0].Movie = Text(Movie(environment: new string('b', 64)));
            Assert.ThrowsExactly<InvalidDataException>(() => StudioVideoExportPlan.ForTimeline(root, 1, 2));
        }

        [TestMethod]
        public void IntermediateAncestorConflictAndFramesOutsideSavedMoviesAreRejected()
        {
            var tree = Tree();
            var last = tree.Add(50, Text(Movie()), tree.OriginalHashes);
            tree.Nodes[2].Movie = Text(MovieV2RangeEditor.Paint(Movie(), 20, 1, "Left", true));
            Assert.ThrowsExactly<InvalidDataException>(() => StudioVideoExportPlan.ForTimeline(tree, 1, last.Id));
            var shortStart = Tree(); shortStart.Nodes[1].Movie = Text(Movie(5));
            Assert.ThrowsExactly<InvalidDataException>(() => StudioVideoExportPlan.ForTimeline(shortStart, 1, 2));
            var shortEnd = Tree(); shortEnd.Nodes[2].Movie = Text(Movie(20));
            Assert.ThrowsExactly<InvalidDataException>(() => StudioVideoExportPlan.ForTimeline(shortEnd, 1, 2));
            var negative = Tree(); negative.Nodes[1].Frame = -1;
            Assert.ThrowsExactly<InvalidDataException>(() => StudioVideoExportPlan.ForTimeline(negative, 1, 2));
        }

        [TestMethod]
        public void PlanningDoesNotMutateTreeAndCopiesItsBaseline()
        {
            var tree = Tree();
            var before = JsonSerializer.Serialize(tree);
            var plan = StudioVideoExportPlan.ForTimeline(tree, 1, 2);
            Assert.AreEqual(before, JsonSerializer.Serialize(tree));
            Assert.AreEqual(tree.Id, plan.TreeId);
            tree.OriginalHashes["user1.dat"] = "changed";
            tree.Nodes[2].Movie = Text(Movie(50));
            Assert.AreEqual("original", plan.OriginalHashes!["user1.dat"]);
            Assert.AreEqual(Text(Movie()), plan.Movie);
            Assert.ThrowsExactly<NotSupportedException>(() => ((IDictionary<string, string>)plan.OriginalHashes).Add("other", "hash"));
        }
    }
}
