using System;
using HollowKnightTAS.Core.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Media
{
    [TestClass]
    public sealed class AudioBlockReframerTests
    {
        [TestMethod]
        public void PreservesEverySampleAcrossFiftyFpsFramesAndZeroLengthRenders()
        {
            var blocks = new AudioBlockReframer(1024, 2);
            int nextInput = 0, nextOutput = 0, zeroRenders = 0;
            for (var frame = 0; frame < 100; frame++)
            {
                var count = blocks.RequiredRenderValues(1920);
                Assert.AreEqual(0, count % 2048);
                if (count == 0) zeroRenders++;
                var input = new float[count];
                for (var i = 0; i < count; i++) input[i] = nextInput++;
                var output = blocks.Consume(input, 1920);
                foreach (var sample in output) Assert.AreEqual((float)nextOutput++, sample);
                Assert.IsTrue(blocks.PendingValues < 2048);
            }
            Assert.IsTrue(zeroRenders > 0);
            Assert.AreEqual(192000, nextOutput);
            Assert.AreEqual(nextInput - nextOutput, blocks.PendingValues);
        }

        [TestMethod]
        public void SupportsVariableFrameCountsAndMultipleBlocks()
        {
            var blocks = new AudioBlockReframer(64, 1);
            int inputIndex = 0, outputIndex = 0;
            foreach (var size in new[] { 0, 65, 1023, 1, 128, 0, 801 })
            {
                var input = new float[blocks.RequiredRenderValues(size)];
                for (var i = 0; i < input.Length; i++) input[i] = inputIndex++;
                foreach (var value in blocks.Consume(input, size)) Assert.AreEqual((float)outputIndex++, value);
            }
            Assert.IsTrue(blocks.PendingValues < 64);
        }

        [TestMethod]
        public void RejectsPartialBlocksAndUnalignedChannelCounts()
        {
            var blocks = new AudioBlockReframer(1024, 2);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => blocks.RequiredRenderValues(1));
            Assert.ThrowsExactly<ArgumentException>(() => blocks.Consume(new float[1920], 1920));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AudioBlockReframer(0, 2));
        }
    }
}
