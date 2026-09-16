using System;
using System.IO;
using HollowKnightTAS.ReferenceObserver;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Verification
{
    [TestClass]
    public sealed class ReferenceObserverActivationTests
    {
        [TestMethod]
        public void ProductionRunBindingDoesNotActivateTestObserver()
        {
            Assert.IsNull(ReferenceObserverOptions.Parse(Array.Empty<string>()));
            Assert.IsNull(ReferenceObserverOptions.Parse(new[] { "--hktas-reference-run=interactive-example" }));
            Assert.IsNull(ReferenceObserverOptions.Parse(new[] { "--hktas-reference-run=cold-restore-example" }));
        }

        [TestMethod]
        public void PartialTestInvocationStillFailsClosed()
        {
            Assert.ThrowsExactly<InvalidDataException>(() => ReferenceObserverOptions.Parse(
                new[] { "--hktas-reference-mode=vanilla-reference" }));
            Assert.ThrowsExactly<InvalidDataException>(() => ReferenceObserverOptions.Parse(
                new[] { "--hktas-reference-run=test", "--hktas-reference-output-base64=eA==" }));
            Assert.ThrowsExactly<InvalidDataException>(() => ReferenceObserverOptions.Parse(
                new[] { "--hktas-reference-run=test", "--hktas-reference-mode=invalid" }));
        }
    }
}
