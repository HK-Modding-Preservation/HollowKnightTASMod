using System.Linq;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Movie
{
    [TestClass]
    public sealed class MovieInputSliceTests
    {
        [TestMethod]
        public void ExtractClipsRunsAndDropsNonInputCommands()
        {
            var span = new MovieSourceSpan("slice.hktas", 1, 1, 1);
            var movie = new MovieDocument(
                "slice.hktas",
                new MovieHeader(
                    MovieProtocolV1.Version,
                    "1.5.78.11833",
                    "1.5.78.11833-77",
                    new string('0', 64),
                    "none",
                    "none",
                    MovieProtocolV1.TickUnit),
                new MovieCommand[]
                {
                    new FrameRunCommand(
                        3,
                        TasAction.Right,
                        0,
                        0,
                        false,
                        span),
                    new CheckpointCommand("middle", span),
                    new FrameRunCommand(
                        4,
                        TasAction.Attack,
                        0,
                        0,
                        false,
                        span)
                });

            var slice = MovieInputSlice.Extract(movie, 2, 3);
            var runs = slice.Commands.Cast<FrameRunCommand>().ToArray();

            Assert.AreEqual(2, runs.Length);
            Assert.AreEqual(1, runs[0].FrameCount);
            Assert.AreEqual(TasAction.Right, runs[0].HeldActions);
            Assert.AreEqual(2, runs[1].FrameCount);
            Assert.AreEqual(TasAction.Attack, runs[1].HeldActions);
        }
    }
}
