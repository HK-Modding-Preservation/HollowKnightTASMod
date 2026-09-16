using System;
using HollowKnightTAS.Core.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Media
{
    [TestClass]
    public sealed class VideoExportTimelineTests
    {
        [TestMethod]
        public void SixtyFpsKeepsStereoValuesSeparateFromSampleFrames()
        {
            var timeline = new VideoExportTimeline(new VideoExportFormat(1920, 1080));
            Assert.AreEqual(6220800, timeline.Format.VideoFrameBytes);
            Assert.AreEqual(0L, timeline.AudioSampleFramesThrough(0));
            Assert.AreEqual(48000L, timeline.AudioSampleFramesThrough(60));
            Assert.AreEqual(1600, timeline.AudioValueCountForFrame(0));
            Assert.AreEqual(2433600L, timeline.AudioSampleFramesThrough(3042));
        }

        [TestMethod]
        public void FractionalRateCarriesRemainderWithoutAccumulatingDrift()
        {
            var timeline = new VideoExportTimeline(new VideoExportFormat(1280, 720, 60000, 1001));
            long total = 0;
            for (var frame = 0; frame < 60000; frame++)
            {
                var count = timeline.AudioValueCountForFrame(frame);
                Assert.IsTrue(count == 1600 || count == 1602);
                total += count;
            }
            Assert.AreEqual(48000L * 1001 * 2, total);
            Assert.AreEqual(total / 2, timeline.AudioSampleFramesThrough(60000));
        }

        [TestMethod]
        public void NormalizesRateAndHandlesLargeIntermediateProducts()
        {
            var timeline = new VideoExportTimeline(new VideoExportFormat(2, 2, 120000, 2002));
            Assert.AreEqual(60000, timeline.Format.FpsNumerator);
            Assert.AreEqual(1001, timeline.Format.FpsDenominator);
            Assert.AreEqual(800800000000000000L, timeline.AudioSampleFramesThrough(1000000000000000L));
        }

        [TestMethod]
        public void RejectsInvalidFormatsAndUnrepresentableCounts()
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new VideoExportFormat(3, 2));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new VideoExportFormat(2, 0));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new VideoExportFormat(2, 2, 0));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new VideoExportFormat(2, 2, 241));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new VideoExportFormat(2, 2, fpsDenominator: 0));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new VideoExportFormat(2, 2, channels: 6));
            var timeline = new VideoExportTimeline(new VideoExportFormat(2, 2));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => timeline.AudioSampleFramesThrough(-1));
            Assert.ThrowsExactly<OverflowException>(() => timeline.AudioSampleFramesThrough(long.MaxValue));
            Assert.ThrowsExactly<OverflowException>(() => timeline.AudioValueCountForFrame(long.MaxValue));
        }
    }
}
