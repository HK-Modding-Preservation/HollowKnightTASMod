using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Movie
{
    [TestClass]
    public sealed class MoviePrefixIdentityTests
    {
        private static readonly MovieSourceSpan Span =
            new MovieSourceSpan("prefix.hktas", 1, 1, 1);

        [TestMethod]
        public void ChangesAfterPrefixRemainCompatible()
        {
            var original = Movie(
                Run(3, TasAction.Right),
                new MarkerCommand("branch", Span),
                Run(2, TasAction.Attack));
            var branch = Movie(
                Run(3, TasAction.Right),
                new MarkerCommand("branch", Span),
                Run(2, TasAction.Jump));

            Assert.IsTrue(
                MoviePrefixIdentity.IsCompatible(
                    original,
                    branch,
                    3));
            Assert.IsFalse(
                MoviePrefixIdentity.IsCompatible(
                    original,
                    branch,
                    5));
        }

        [TestMethod]
        public void InputOrEventChangeInsidePrefixIsRejected()
        {
            var original = Movie(
                Run(1, TasAction.Right),
                new CheckpointCommand("safe", Span),
                Run(1, TasAction.Right),
                Run(2, TasAction.Attack));
            var inputChanged = Movie(
                Run(1, TasAction.Right),
                Run(1, TasAction.Left),
                new CheckpointCommand("safe", Span),
                Run(2, TasAction.Attack));
            var eventChanged = Movie(
                Run(1, TasAction.Right),
                new CheckpointCommand("other", Span),
                Run(1, TasAction.Right),
                Run(2, TasAction.Attack));

            Assert.IsFalse(
                MoviePrefixIdentity.IsCompatible(
                    original,
                    inputChanged,
                    2));
            Assert.IsFalse(
                MoviePrefixIdentity.IsCompatible(
                    original,
                    eventChanged,
                    2));
        }

        [TestMethod]
        public void ZeroInputPrefixAllowsEarlyEditsButRejectsAnotherEnvironment()
        {
            var original = Movie(Run(5, TasAction.None));
            var edited = Movie(Run(100, TasAction.Right));
            var otherEnvironment = new MovieDocument(
                "other.hktas",
                new MovieHeader(MovieProtocolV1.Version, "1.5.78.11833",
                    "1.5.78.11833-77", new string('1', 64), "none", "none",
                    MovieProtocolV1.TickUnit), edited.Commands);
            Assert.IsTrue(MoviePrefixIdentity.IsCompatible(original, edited, 0));
            Assert.IsFalse(MoviePrefixIdentity.IsCompatible(original, edited, 5));
            Assert.IsFalse(MoviePrefixIdentity.IsCompatible(original, otherEnvironment, 0));
        }

        private static MovieDocument Movie(params MovieCommand[] commands)
        {
            return new MovieDocument(
                "prefix.hktas",
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
    }
}
