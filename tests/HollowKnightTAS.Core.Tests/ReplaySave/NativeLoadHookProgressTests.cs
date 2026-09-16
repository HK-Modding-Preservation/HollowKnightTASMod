using System;
using HollowKnightTAS.Core.ReplaySave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.ReplaySave
{
    [TestClass]
    public sealed class NativeLoadHookProgressTests
    {
        [TestMethod]
        public void NativeDataThenSlotCompletesExactlyOnce()
        {
            var progress = new NativeLoadHookProgress(4);
            progress.ObserveDataLoaded();
            Assert.IsFalse(progress.IsComplete);
            progress.ObserveSlotConfirmed(4);
            Assert.IsTrue(progress.IsComplete);
            Assert.ThrowsExactly<InvalidOperationException>(() => progress.ObserveSlotConfirmed(4));
            Assert.IsFalse(progress.IsComplete);
        }

        [TestMethod]
        public void WrongOrderOrSlotCannotRecoverIntoSuccess()
        {
            var reversed = new NativeLoadHookProgress(4);
            Assert.ThrowsExactly<InvalidOperationException>(() => reversed.ObserveSlotConfirmed(4));
            Assert.ThrowsExactly<InvalidOperationException>(() => reversed.ObserveDataLoaded());
            var wrong = new NativeLoadHookProgress(4);
            wrong.ObserveDataLoaded();
            Assert.ThrowsExactly<InvalidOperationException>(() => wrong.ObserveSlotConfirmed(3));
            Assert.ThrowsExactly<InvalidOperationException>(() => wrong.ObserveSlotConfirmed(4));
            Assert.IsFalse(wrong.IsComplete);
        }

        [TestMethod]
        public void DuplicateDataCallbackFailsBeforeSlotConfirmation()
        {
            var progress = new NativeLoadHookProgress(1);
            progress.ObserveDataLoaded();
            Assert.ThrowsExactly<InvalidOperationException>(() => progress.ObserveDataLoaded());
            Assert.ThrowsExactly<InvalidOperationException>(() => progress.ObserveSlotConfirmed(1));
        }
    }
}
