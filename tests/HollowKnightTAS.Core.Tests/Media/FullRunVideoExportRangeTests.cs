using System;
using HollowKnightTAS.Runtime.FullRun;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Media
{
    [TestClass]
    public sealed class FullRunVideoExportRangeTests
    {
        [TestMethod]
        public void FullSingleInputMovieIncludesItsOnlyFrameOnce()
        {
            var range = new FullRunVideoExportRange(1, 0, 1, -1, 10);
            Assert.AreEqual(0L, range.StartMovieFrame);
            Assert.AreEqual(1L, range.EndMovieFrame);
            Assert.IsTrue(range.CompleteNativeFrame(2, 1));
            Assert.ThrowsExactly<InvalidOperationException>(() => range.CompleteNativeFrame(2, 1));
            Assert.ThrowsExactly<InvalidOperationException>(() => range.CompleteNativeFrame(3, 1));
        }

        [TestMethod]
        public void LoadingFramesAreCapturedWithoutConsumingMovieInput()
        {
            var range = new FullRunVideoExportRange(500, 10, 100, 12, 20);
            Assert.IsFalse(range.CompleteNativeFrame(501, 10));
            Assert.IsFalse(range.CompleteNativeFrame(502, 10));
            Assert.IsFalse(range.CompleteNativeFrame(503, 11));
            Assert.IsFalse(range.CompleteNativeFrame(504, 11));
            Assert.IsTrue(range.CompleteNativeFrame(505, 12));
        }

        [TestMethod]
        public void SelectedEndCanPrecedeLoadedMovieTail()
        {
            var range = new FullRunVideoExportRange(200, 20, 100, 22, 20);
            Assert.IsFalse(range.CompleteNativeFrame(201, 21));
            Assert.IsTrue(range.CompleteNativeFrame(202, 22));
            Assert.AreEqual(22L, range.EndMovieFrame);
            Assert.ThrowsExactly<InvalidOperationException>(() => range.CompleteNativeFrame(203, 23));
        }

        [TestMethod]
        public void DuplicateSkippedOrRewoundNativeBoundariesAreRejected()
        {
            foreach (var native in new[] { 4L, 5L, 7L })
            {
                var range = new FullRunVideoExportRange(5, 1, 10, 3, 10);
                Assert.ThrowsExactly<InvalidOperationException>(() => range.CompleteNativeFrame(native, 2));
            }
        }

        [TestMethod]
        public void SkippedOrRewoundMovieInputIsRejected()
        {
            foreach (var movie in new[] { 1L, 4L })
            {
                var range = new FullRunVideoExportRange(5, 2, 10, 3, 10);
                Assert.ThrowsExactly<InvalidOperationException>(() => range.CompleteNativeFrame(6, movie));
            }
        }

        [TestMethod]
        [DataRow(2, 2)]
        [DataRow(2, 1)]
        [DataRow(2, 11)]
        [DataRow(2, -2)]
        [DataRow(10, -1)]
        [DataRow(11, -1)]
        public void EmptyReverseOrOutOfBoundsRangeIsRejected(int start, int end)
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                new FullRunVideoExportRange(5, start, 10, end, 100));
        }

        [TestMethod]
        public void SafetyLimitMustHaveRoomForLoadingFrames()
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                new FullRunVideoExportRange(5, 2, 10, 5, 3));
            var range = new FullRunVideoExportRange(5, 2, 10, 5, 4);
            Assert.AreEqual(5L, range.EndMovieFrame);
        }
    }
}
