using System;
using System.IO;
using System.Linq;
using HollowKnightTAS.Companion.Automation;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class MoviePatchWorkspaceTests
    {
        [TestMethod]
        public void TypedEditCreatesContentAddressedChildBranch()
        {
            var root = TemporaryDirectory();
            try
            {
                var workspace = new MoviePatchWorkspace(root, "session-a");
                var baseBytes = MovieBytes(
                    Run(5, TasAction.Right),
                    new CheckpointCommand("tail", Span));
                var baseValidation = workspace.Validate(baseBytes);
                var replacement = MovieBytes(Run(2, TasAction.Attack));

                var result = workspace.EditTimeline(
                    TimelineEditKind.Replace,
                    baseValidation.BranchMovieId,
                    baseValidation.BranchMovieId,
                    baseBytes,
                    1,
                    2,
                    replacement);

                Assert.IsTrue(result.Success, result.Detail);
                Assert.AreEqual(
                    baseValidation.BranchMovieId,
                    result.ParentMovieId);
                Assert.AreEqual(5, result.ExpandedTicks);
                Assert.AreEqual(0, result.TickDelta);
                CollectionAssert.AreEqual(
                    workspace.ReadBranch(result.BranchMovieId),
                    result.CanonicalMovieUtf8!);
                var parentPath = Path.Combine(
                    root,
                    "session-a",
                    "movie-branches",
                    result.BranchMovieId + ".parents");
                CollectionAssert.Contains(
                    File.ReadAllLines(parentPath),
                    baseValidation.BranchMovieId);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void TypedEditRejectsStaleBaseAndNonInputReplacement()
        {
            var root = TemporaryDirectory();
            try
            {
                var workspace = new MoviePatchWorkspace(root, "session-b");
                var baseBytes = MovieBytes(Run(3, TasAction.Left));
                var baseId = workspace.Validate(baseBytes).BranchMovieId;
                var stale = workspace.EditTimeline(
                    TimelineEditKind.Delete,
                    new string('a', 64),
                    baseId,
                    baseBytes,
                    0,
                    1,
                    null);
                Assert.AreEqual("BaseMovieChanged", stale.Code);

                var eventReplacement = MovieBytes(
                    Run(1, TasAction.Jump),
                    new MarkerCommand("not-input", Span));
                var invalid = workspace.EditTimeline(
                    TimelineEditKind.Insert,
                    baseId,
                    baseId,
                    baseBytes,
                    1,
                    0,
                    eventReplacement);
                Assert.AreEqual(
                    "InputOnlyReplacementRequired",
                    invalid.Code);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static readonly MovieSourceSpan Span =
            new MovieSourceSpan("workspace-test.hktas", 1, 1, 1);

        private static byte[] MovieBytes(params MovieCommand[] commands)
        {
            var movie = new MovieDocument(
                "workspace-test.hktas",
                new MovieHeader(
                    MovieProtocolV1.Version,
                    "1.5.78.11833",
                    "1.5.78.11833-77",
                    new string('0', 64),
                    "none",
                    "none",
                    MovieProtocolV1.TickUnit),
                commands);
            return new MovieCanonicalWriter().WriteUtf8(movie);
        }

        private static FrameRunCommand Run(
            long count,
            TasAction held)
        {
            return new FrameRunCommand(
                count,
                held,
                0,
                0,
                false,
                Span);
        }

        private static string TemporaryDirectory()
        {
            var result = Path.Combine(
                Path.GetTempPath(),
                "hktas-workspace-tests-"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(result);
            return result;
        }
    }
}
