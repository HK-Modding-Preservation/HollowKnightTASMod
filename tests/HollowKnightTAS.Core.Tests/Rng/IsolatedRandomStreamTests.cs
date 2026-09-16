using System;
using HollowKnightTAS.Runtime.Rng;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Rng
{
    [TestClass]
    public class IsolatedRandomStreamTests
    {
        [TestMethod]
        public void RenderingContinuesPrivateSequenceWithoutConsumingGameplay()
        {
            var state = 10;
            var stream = new IsolatedRandomStream<int>(() => state, value => state = value);
            stream.Run(() => state += 3);
            Assert.AreEqual(10, state);
            state = 80;
            stream.Run(() => { Assert.AreEqual(13, state); state += 2; });
            Assert.AreEqual(80, state);
        }

        [TestMethod]
        public void ThrowingRendererRestoresGameplayAndCanBeCalledAgain()
        {
            var state = 10;
            var stream = new IsolatedRandomStream<int>(() => state, value => state = value);
            Assert.ThrowsExactly<InvalidOperationException>(() => stream.Run(() =>
            { state++; throw new InvalidOperationException(); }));
            Assert.AreEqual(10, state);
            stream.Run(() => Assert.AreEqual(11, state));
            Assert.AreEqual(10, state);
        }

        [TestMethod]
        public void NestedRendererCallsOriginalOnceAndSharesPrivateSequence()
        {
            var state = 10;
            var calls = 0;
            var stream = new IsolatedRandomStream<int>(() => state, value => state = value);
            stream.Run(() => { state++; stream.Run(() => { calls++; state++; }); });
            Assert.AreEqual(1, calls);
            Assert.AreEqual(10, state);
            stream.Run(() => Assert.AreEqual(12, state));
        }
    }
}
