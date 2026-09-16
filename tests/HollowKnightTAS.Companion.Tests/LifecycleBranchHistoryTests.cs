using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class LifecycleBranchHistoryTests
    {
        private static string Id(char c) => new string(c, 64);
        [TestMethod]
        public void UndoRedoReturnsOriginalAndDropsAbandonedFuture()
        {
            var history = new LifecycleBranchHistory();
            history.Record(Id('a'), Id('b'));
            history.Record(Id('b'), Id('c'));
            Assert.IsTrue(history.TryUndo(Id('c'), out var previous));
            Assert.AreEqual(Id('b'), previous);
            Assert.IsTrue(history.TryUndo(previous, out previous));
            Assert.AreEqual(Id('a'), previous);
            Assert.IsTrue(history.TryRedo(previous, out previous));
            Assert.AreEqual(Id('b'), previous);
            history.Record(previous, Id('d'));
            Assert.IsFalse(history.TryRedo(Id('d'), out _));
            Assert.IsTrue(history.TryUndo(Id('d'), out previous));
            Assert.AreEqual(Id('b'), previous);
        }

        [TestMethod]
        public void ManualSelectionCannotUndoAnUnrelatedBranch()
        {
            var history = new LifecycleBranchHistory();
            history.Record(Id('a'), Id('b'));
            Assert.IsFalse(history.TryUndo(Id('c'), out var selected));
            Assert.AreEqual(Id('c'), selected);
            Assert.IsFalse(history.TryRedo(Id('b'), out _));
        }
    }
}
