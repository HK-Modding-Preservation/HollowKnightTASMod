using System;
using HollowKnightTAS.Cli.Commands.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Cli
{
    [TestClass]
    public sealed class AutomationWatchCommandTests
    {
        [TestMethod]
        public void ParseWatchOptions_DefaultsToOneIteration()
        {
            var options =
                AutomationCommands.ParseWatchOptions(
                    Array.Empty<string>());

            Assert.AreEqual(0L, options.FromMovieTick);
            Assert.AreEqual(100, options.Count);
            Assert.AreEqual(1, options.Iterations);
            Assert.IsNull(options.Duration);
        }

        [TestMethod]
        public void ParseWatchOptions_DurationUsesExactSeconds()
        {
            var options =
                AutomationCommands.ParseWatchOptions(
                    new[]
                    {
                        "--from=42",
                        "--count=200",
                        "--duration-seconds=3600"
                    });

            Assert.AreEqual(42L, options.FromMovieTick);
            Assert.AreEqual(200, options.Count);
            Assert.IsNull(options.Iterations);
            Assert.AreEqual(
                TimeSpan.FromSeconds(3600),
                options.Duration);
        }

        [TestMethod]
        public void ParseWatchOptions_AllowsPreInputMovieTick()
        {
            var options =
                AutomationCommands.ParseWatchOptions(
                    new[] { "--from=-1" });

            Assert.AreEqual(-1L, options.FromMovieTick);
        }

        [TestMethod]
        public void HasWatchCompleted_DurationNeverStopsEarly()
        {
            var options =
                AutomationCommands.ParseWatchOptions(
                    new[] { "--duration-seconds=3600" });

            Assert.IsFalse(
                AutomationCommands.HasWatchCompleted(
                    options,
                    100000,
                    TimeSpan.FromMilliseconds(3599999)));
            Assert.IsTrue(
                AutomationCommands.HasWatchCompleted(
                    options,
                    1,
                    TimeSpan.FromSeconds(3600)));
        }

        [TestMethod]
        public void HasWatchCompleted_IterationsRemainCompatible()
        {
            var options =
                AutomationCommands.ParseWatchOptions(
                    new[] { "--iterations=2" });

            Assert.IsFalse(
                AutomationCommands.HasWatchCompleted(
                    options,
                    1,
                    TimeSpan.FromDays(1)));
            Assert.IsTrue(
                AutomationCommands.HasWatchCompleted(
                    options,
                    2,
                    TimeSpan.Zero));
        }

        [TestMethod]
        public void ParseWatchOptions_IterationsAndDurationAreExclusive()
        {
            var exception = Assert.ThrowsExactly<ArgumentException>(
                () => AutomationCommands.ParseWatchOptions(
                    new[]
                    {
                        "--iterations=2",
                        "--duration-seconds=1"
                    }));

            StringAssert.Contains(
                exception.Message,
                "mutually exclusive");
        }

        [TestMethod]
        [DataRow("--duration-seconds=0")]
        [DataRow("--duration-seconds=86401")]
        [DataRow("--iterations=0")]
        [DataRow("--iterations=100001")]
        [DataRow("--from=-2")]
        public void ParseWatchOptions_RejectsOutOfRangeLimits(
            string argument)
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => AutomationCommands.ParseWatchOptions(
                    new[] { argument }));
        }
    }
}
