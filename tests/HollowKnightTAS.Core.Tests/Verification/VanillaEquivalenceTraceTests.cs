using System.Collections.Generic;
using HollowKnightTAS.Core.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Verification
{
    [TestClass]
    public sealed class VanillaEquivalenceTraceTests
    {
        [TestMethod]
        public void ComparisonIgnoresDiagnosticWallClockFields()
        {
            var expected = Frame(1f, 10L);
            var actual = Frame(1f, 999L);

            var difference = VanillaEquivalenceTraceComparer.FirstDifference(
                new[] { expected },
                new[] { actual });

            Assert.IsNull(difference);
            Assert.AreEqual(
                VanillaEquivalenceFrameJson.ComputeComparisonSha256(expected),
                VanillaEquivalenceFrameJson.ComputeComparisonSha256(actual));
        }

        [TestMethod]
        public void ComparisonReportsFirstGameplayFieldDifference()
        {
            var expected = Frame(1f, 10L);
            var actual = Frame(2f, 10L);

            var difference = VanillaEquivalenceTraceComparer.FirstDifference(
                new[] { expected },
                new[] { actual });

            Assert.IsNotNull(difference);
            Assert.AreEqual(0L, difference.LogicalTick);
            Assert.AreEqual("hero.position.x", difference.Key);
        }

        [TestMethod]
        public void ComparisonRejectsMissingFrames()
        {
            var difference = VanillaEquivalenceTraceComparer.FirstDifference(
                new[] { Frame(1f, 10L) },
                new List<VanillaEquivalenceFrame>());

            Assert.IsNotNull(difference);
            Assert.AreEqual("$frameCount", difference.Key);
        }

        [TestMethod]
        public void JsonIncludesCanonicalComparisonHashAndFieldPolicy()
        {
            var json = VanillaEquivalenceFrameJson.Serialize(
                Frame(1f, 10L));

            StringAssert.Contains(json, "\"comparisonSha256\":");
            StringAssert.Contains(json, "\"comparable\":true");
            StringAssert.Contains(json, "\"comparable\":false");
        }

        [TestMethod]
        public void LogicalTimelineDoesNotDependOnAbsoluteProcessClock()
        {
            var first = new VanillaTimelineAccumulator();
            var second = new VanillaTimelineAccumulator();

            var firstSamples = new[]
            {
                first.Advance(3516, 0.02f, 0.021f, 0.02f),
                first.Advance(3517, 0.02f, 0.019f, 0.02f),
                first.Advance(3519, 0.02f, 0.022f, 0.02f)
            };
            var secondSamples = new[]
            {
                second.Advance(987, 0.02f, 0.021f, 0.02f),
                second.Advance(988, 0.02f, 0.019f, 0.02f),
                second.Advance(990, 0.02f, 0.022f, 0.02f)
            };

            for (var index = 0; index < firstSamples.Length; index++)
            {
                Assert.AreEqual(
                    firstSamples[index].FixedSteps,
                    secondSamples[index].FixedSteps);
                Assert.AreEqual(
                    firstSamples[index].RelativeTime,
                    secondSamples[index].RelativeTime);
                Assert.AreEqual(
                    firstSamples[index].UnscaledRelativeTime,
                    secondSamples[index].UnscaledRelativeTime);
                Assert.AreEqual(
                    firstSamples[index].FixedRelativeTime,
                    secondSamples[index].FixedRelativeTime);
            }
            Assert.AreEqual(2, firstSamples[2].FixedSteps);
            Assert.AreEqual(0.04f, firstSamples[2].RelativeTime);
            Assert.AreEqual(0.041f, firstSamples[2].UnscaledRelativeTime);
            Assert.AreEqual(0.06f, firstSamples[2].FixedRelativeTime);
        }

        [TestMethod]
        public void LogicalTimelineRejectsRegressingFixedTicks()
        {
            var timeline = new VanillaTimelineAccumulator();
            timeline.Advance(10, 0.02f, 0.02f, 0.02f);
            try
            {
                timeline.Advance(9, 0.02f, 0.02f, 0.02f);
            }
            catch (System.InvalidOperationException)
            {
                return;
            }

            Assert.Fail("Expected a regressing fixed tick to be rejected.");
        }

        private static VanillaEquivalenceFrame Frame(
            float heroX,
            long wallClock)
        {
            var builder = new VanillaEquivalenceFrameBuilder();
            builder.AddFloat32("hero.position.x", heroX);
            builder.AddInt32("input.held", 2);
            builder.AddInt64(
                "diagnostic.wallClockTicks",
                wallClock,
                comparable: false);
            return builder.Build(1, 0);
        }
    }
}
