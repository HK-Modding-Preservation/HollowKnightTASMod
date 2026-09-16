using System;
using System.Collections.Generic;
using System.Linq;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Movie
{
    [TestClass]
    public sealed class MovieTimelineEditorTests
    {
        [TestMethod]
        public void ReplaceSplitsRunsAndInvalidatesDeletedCheckpoint()
        {
            var movie = Movie(
                Run(3, TasAction.Left),
                new CheckpointCommand("inside", Span),
                Run(3, TasAction.Right),
                new MarkerCommand("tail", Span),
                Run(2, TasAction.None));

            var result = MovieTimelineEditor.Replace(
                movie,
                2,
                3,
                new[] { Run(2, TasAction.Jump) });

            CollectionAssert.AreEqual(
                new[]
                {
                    "2:left",
                    "2:jump",
                    "1:right",
                    "event:tail",
                    "2:-"
                },
                Describe(result.Movie));
            Assert.AreEqual(8, result.PreviousExpandedTicks);
            Assert.AreEqual(7, result.ExpandedTicks);
            Assert.AreEqual(-1, result.TickDelta);
            CollectionAssert.AreEqual(
                new[] { "inside" },
                result.InvalidatedCheckpoints.ToArray());
        }

        [TestMethod]
        public void InsertOccursBeforeCommandsAtTheInsertionAnchor()
        {
            var movie = Movie(
                Run(2, TasAction.Left),
                new CheckpointCommand("anchor", Span),
                Run(2, TasAction.Right));

            var result = MovieTimelineEditor.Insert(
                movie,
                2,
                new[] { Run(1, TasAction.Attack) });

            CollectionAssert.AreEqual(
                new[]
                {
                    "2:left",
                    "1:attack",
                    "checkpoint:anchor",
                    "2:right"
                },
                Describe(result.Movie));
            Assert.AreEqual(1, result.TickDelta);
        }

        [TestMethod]
        public void DeleteSupportsWholeMovieAndKeepsTrailingEvents()
        {
            var movie = Movie(
                Run(4, TasAction.Dash),
                new MarkerCommand("complete", Span));

            var result = MovieTimelineEditor.Delete(movie, 0, 4);

            CollectionAssert.AreEqual(
                new[] { "event:complete" },
                Describe(result.Movie));
            Assert.AreEqual(0, result.ExpandedTicks);
        }

        [TestMethod]
        public void LargeRunEditDoesNotRequirePerTickExpansion()
        {
            var movie = Movie(
                Run(10_000_000, TasAction.Right));

            var result = MovieTimelineEditor.Replace(
                movie,
                4_999_999,
                2,
                new[] { Run(1, TasAction.Jump) });

            CollectionAssert.AreEqual(
                new[]
                {
                    "4999999:right",
                    "1:jump",
                    "4999999:right"
                },
                Describe(result.Movie));
            Assert.AreEqual(9_999_999, result.ExpandedTicks);
        }

        [TestMethod]
        public void InvalidRangesFailBeforeProducingAMovie()
        {
            var movie = Movie(Run(2, TasAction.Left));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => MovieTimelineEditor.Delete(movie, 2, 1));
            Assert.ThrowsExactly<ArgumentException>(
                () => MovieTimelineEditor.Insert(
                    movie,
                    0,
                    Array.Empty<FrameRunCommand>()));
        }

        private static readonly MovieSourceSpan Span =
            new MovieSourceSpan("timeline-test.hktas", 1, 1, 1);

        private static MovieDocument Movie(params MovieCommand[] commands)
        {
            return new MovieDocument(
                "timeline-test.hktas",
                new MovieHeader(
                    MovieProtocolV1.Version,
                    "1.5.78.11833",
                    "1.5.78.11833-77",
                    new string('0', 64),
                    "none",
                    "none",
                    MovieProtocolV1.TickUnit),
                commands);
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

        private static string[] Describe(MovieDocument movie)
        {
            var result = new List<string>();
            foreach (var command in movie.Commands)
            {
                switch (command)
                {
                    case FrameRunCommand frames:
                        result.Add(
                            frames.FrameCount
                            + ":"
                            + ActionName(frames.HeldActions));
                        break;
                    case CheckpointCommand checkpoint:
                        result.Add(
                            "checkpoint:" + checkpoint.Identifier);
                        break;
                    case MarkerCommand marker:
                        result.Add("event:" + marker.Text);
                        break;
                }
            }

            return result.ToArray();
        }

        private static string ActionName(TasAction action)
        {
            if (action == TasAction.None)
            {
                return "-";
            }

            return MovieProtocolV1.OrderedActions
                .Where(value => (action & value) != 0)
                .Select(MovieProtocolV1.GetActionName)
                .Aggregate((left, right) => left + "," + right);
        }
    }
}
