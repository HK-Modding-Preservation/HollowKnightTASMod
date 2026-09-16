using System;
using System.IO;
using System.Text;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.ReplaySave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.ReplaySave
{
    [TestClass]
    public sealed class ReplayLifecycleCursorTests
    {
        private static ReplayLifecycleCursor Cursor()
        {
            var commit = ReplaySaveTestFactory.Commit();
            var movie = new MovieParser().Parse(new StringReader(Encoding.UTF8.GetString(
                commit.Objects[commit.Descriptor.MovieObjectSha256])), "cursor.hktas").Document!;
            var prefix = MoviePrefixIdentity.ComputeSha256(movie, 3);
            return new ReplayLifecycleCursor(new ReplayLifecycleLog(commit.Descriptor.BaselineObjectSha256,
                new[] {
                    new ReplayLifecycleRecord(0, 2, prefix, ReplayLifecycleKind.ReturnToMenu, 0, "", ReplayLifecycleOutcome.Completed, 20, ""),
                    new ReplayLifecycleRecord(1, 2, prefix, ReplayLifecycleKind.LoadSlot, 4, new string('a',64), ReplayLifecycleOutcome.Completed, 100, "")
                }), movie);
        }

        [TestMethod]
        public void SameBoundaryOperationsRunOnceInOrderBeforeNextInput()
        {
            var cursor = Cursor();
            Assert.IsTrue(cursor.CanAdvanceInput(1));
            Assert.IsNull(cursor.Claim(1, 10));
            Assert.IsFalse(cursor.CanAdvanceInput(2));
            Assert.AreEqual(0, cursor.Claim(2, 11)!.Sequence);
            Assert.IsNull(cursor.Claim(2, 12));
            Assert.ThrowsExactly<InvalidOperationException>(() => cursor.Complete(1, 20));
            cursor.Complete(0, 31);
            Assert.IsFalse(cursor.CanAdvanceInput(2));
            Assert.AreEqual(1, cursor.Claim(2, 31)!.Sequence);
            cursor.Complete(1, 131);
            Assert.AreEqual(100L, cursor.LastNativeFrameCount);
            Assert.IsTrue(cursor.IsComplete);
            Assert.IsTrue(cursor.CanAdvanceInput(2));
            Assert.ThrowsExactly<InvalidOperationException>(() => cursor.Complete(1, 132));
        }

        [TestMethod]
        public void FailureOrSkippedBoundaryNeverReleasesInputOrRetries()
        {
            var failed = Cursor();
            failed.Claim(2, 10);
            failed.Fail(0, "Native request failed.");
            Assert.IsNull(failed.Claim(2, 11));
            Assert.IsFalse(failed.CanAdvanceInput(2));
            Assert.IsFalse(failed.IsComplete);
            Assert.AreEqual(0, failed.CompletedCount);
            var skipped = Cursor();
            Assert.ThrowsExactly<InvalidOperationException>(() => skipped.Claim(3, 12));
            Assert.IsFalse(skipped.CanAdvanceInput(3));
            var moved = Cursor();
            moved.Claim(2, 10);
            Assert.ThrowsExactly<InvalidOperationException>(() => moved.CanAdvanceInput(3));
        }
    }
}
