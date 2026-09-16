using System;
using System.IO;
using System.Linq;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class InputGridEditorTests
    {
        [TestMethod]
        public void PageIsBoundedAndUsesInputTicksFromCanonicalFixture()
        {
            var movie = ParseFixture();

            var page = InputGridEditor.Page(movie, 9, 10);

            Assert.AreEqual(16, page.Count);
            Assert.AreEqual(9, page[0].Tick);
            Assert.AreEqual(24, page[15].Tick);
            Assert.AreEqual("▶", page[1].Current);
            page[1].UpdateCurrent(11);
            Assert.AreEqual("", page[1].Current);
            Assert.IsTrue(page.Any(row => row.Jump));
            Assert.AreEqual("2500, -7500", InputGridEditor.Page(movie, 22, 3)[0].Axes);
        }

        [TestMethod]
        public void PageAndToggleDoNotExpandLargeRunBeyondVisiblePageOrRunCount()
        {
            var movie = Movie(new FrameRunCommand(
                1_000_000_000L,
                TasAction.Right,
                123,
                -456,
                true,
                Span));

            var page = InputGridEditor.Page(movie, 999_999_995L, 999_999_997L);
            Assert.AreEqual(5, page.Count);
            Assert.AreEqual(999_999_995L, page[0].Tick);
            Assert.AreEqual(123, page[0].Input.AxisX);
            Assert.AreEqual(InputGridEditor.PageSize, InputGridEditor.Page(movie, 500, 1_000).Count);

            var toggled = InputGridEditor.Toggle(movie, 10, 500, TasAction.Attack);
            var runs = toggled.Commands.OfType<FrameRunCommand>().ToArray();
            Assert.AreEqual(3, runs.Length);
            Assert.AreEqual(10, runs[0].FrameCount);
            Assert.AreEqual(500, runs[1].FrameCount);
            Assert.AreEqual(999_999_490L, runs[2].FrameCount);
            Assert.AreEqual(TasAction.Right, runs[0].HeldActions);
            Assert.AreEqual(TasAction.Right | TasAction.Attack, runs[1].HeldActions);
            Assert.AreEqual(TasAction.Right, runs[2].HeldActions);
            Assert.IsTrue(runs.All(run => run.HasAnalogAxes && run.AxisX == 123 && run.AxisY == -456));
        }

        [TestMethod]
        public void ToggleMixedSelectionSetsColumnAndPreservesOtherKeysAndAnalog()
        {
            var movie = Movie(
                Run(2, TasAction.Right, 100, 200, true),
                Run(3, TasAction.Right | TasAction.Attack, 300, 400, true));

            var edited = InputGridEditor.Toggle(movie, 1, 3, TasAction.Attack);
            var frames = Expand(edited).ToArray();

            Assert.AreEqual(5, frames.Length);
            Assert.AreEqual(TasAction.Right, frames[0].HeldActions);
            Assert.AreEqual(100, frames[0].AxisX);
            Assert.AreEqual(TasAction.Right | TasAction.Attack, frames[1].HeldActions);
            Assert.AreEqual(100, frames[1].AxisX);
            Assert.IsTrue(frames.Skip(2).All(frame =>
                frame.HeldActions == (TasAction.Right | TasAction.Attack)
                && frame.AxisX == 300
                && frame.AxisY == 400
                && frame.HasAnalogAxes));
        }

        [TestMethod]
        public void ToggleAllSetClearsOnlyRequestedColumnAndRetainsAxes()
        {
            var movie = Movie(Run(4, TasAction.Left | TasAction.Attack, -11, 22, true));

            var edited = InputGridEditor.Toggle(movie, 1, 2, TasAction.Attack);
            var frames = Expand(edited).ToArray();

            Assert.AreEqual(4, frames.Length);
            Assert.AreEqual(TasAction.Left | TasAction.Attack, frames[0].HeldActions);
            Assert.IsTrue(frames[1].HeldActions == TasAction.Left);
            Assert.IsTrue(frames[2].HeldActions == TasAction.Left);
            Assert.AreEqual(TasAction.Left | TasAction.Attack, frames[3].HeldActions);
            Assert.IsTrue(frames.All(frame =>
                frame.AxisX == -11 && frame.AxisY == 22 && frame.HasAnalogAxes));
        }

        [TestMethod]
        public void PageToggleAndPasteRejectOutOfRangeSelections()
        {
            var movie = Movie(Run(3, TasAction.None));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => InputGridEditor.Page(movie, 4, 0));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => InputGridEditor.Toggle(movie, 2, 2, TasAction.Left));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => InputGridEditor.Paste(movie, 2, 2, Movie(Run(2, TasAction.Right))));
        }

        [TestMethod]
        public void PasteRejectsEventsAndLeavesTargetHeaderUnchanged()
        {
            var target = Movie(Run(4, TasAction.Left), new CheckpointCommand("keep", Span));
            var clipboard = Movie(Run(1, TasAction.Dash), new MarkerCommand("not input", Span));
            var header = target.Header;

            var originalCommands = target.Commands.ToArray();
            Assert.ThrowsExactly<ArgumentException>(
                () => InputGridEditor.Paste(target, 1, 1, clipboard));
            Assert.AreSame(header, target.Header);
            Assert.AreEqual("none", target.Header.BaselineId);
            Assert.AreEqual(2, target.Commands.Count);
            CollectionAssert.AreEqual(originalCommands, target.Commands.ToArray());
        }

        [TestMethod]
        public void SuccessfulPastePreservesTargetHeaderAndReplacesInputOnly()
        {
            var target = Movie(Run(4, TasAction.Left), new CheckpointCommand("keep", Span));
            var clipboard = Movie(Run(2, TasAction.Dash, 7, 8, true));
            var header = target.Header;

            var edited = InputGridEditor.Paste(target, 1, 1, clipboard);

            Assert.AreSame(header, edited.Header);
            Assert.AreEqual(5, InputGridEditor.Count(edited));
            var frames = Expand(edited).ToArray();
            Assert.AreEqual(TasAction.Left, frames[0].HeldActions);
            Assert.AreEqual(TasAction.Dash, frames[1].HeldActions);
            Assert.AreEqual(TasAction.Dash, frames[2].HeldActions);
            Assert.AreEqual(TasAction.Left, frames[3].HeldActions);
            Assert.AreEqual(TasAction.Left, frames[4].HeldActions);
            Assert.IsTrue(frames[1].HasAnalogAxes && frames[1].AxisX == 7 && frames[1].AxisY == 8);
            Assert.IsNotNull(edited.Commands.OfType<CheckpointCommand>().SingleOrDefault(x => x.Identifier == "keep"));
        }

        private static MovieDocument ParseFixture()
        {
            var path = Path.Combine(
                AppContext.BaseDirectory,
                "fixtures",
                "actions-v1.canonical.hktas");
            using var reader = File.OpenText(path);
            var result = new MovieParser().Parse(reader, path);
            Assert.IsTrue(result.Success);
            return result.Document!;
        }

        private static MovieDocument Movie(params MovieCommand[] commands) =>
            new MovieDocument(
                "input-grid-test.hktas",
                new MovieHeader(
                    MovieProtocolV1.Version,
                    "1.5.78.11833",
                    "1.5.78.11833-77",
                    new string('0', 64),
                    "none",
                    "none",
                    MovieProtocolV1.TickUnit),
                commands);

        private static FrameRunCommand Run(
            long count,
            TasAction held,
            int axisX = 0,
            int axisY = 0,
            bool hasAnalogAxes = false) =>
            new FrameRunCommand(count, held, axisX, axisY, hasAnalogAxes, Span);

        private static System.Collections.Generic.IEnumerable<FrameRunCommand> Expand(MovieDocument movie)
        {
            foreach (var run in movie.Commands.OfType<FrameRunCommand>())
            {
                for (var i = 0L; i < run.FrameCount; i++)
                {
                    yield return new FrameRunCommand(
                        1,
                        run.HeldActions,
                        run.AxisX,
                        run.AxisY,
                        run.HasAnalogAxes,
                        run.Span);
                }
            }
        }

        private static readonly MovieSourceSpan Span =
            new MovieSourceSpan("input-grid-test.hktas", 1, 1, 1);
    }
}
