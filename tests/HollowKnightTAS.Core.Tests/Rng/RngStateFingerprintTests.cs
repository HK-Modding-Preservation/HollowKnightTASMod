using System;
using HollowKnightTAS.Core.Rng;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Rng
{
    [TestClass]
    public sealed class RngStateFingerprintTests
    {
        [TestMethod]
        public void FromBytes_RepeatedOneHundredTimesIsStable()
        {
            var bytes = new byte[]
            {
                0x01, 0x23, 0x45, 0x67,
                0x89, 0xab, 0xcd, 0xef,
                0xfe, 0xdc, 0xba, 0x98,
                0x76, 0x54, 0x32, 0x10
            };
            var expected = RngStateFingerprint.FromBytes(
                "codec-v1",
                bytes);

            for (var index = 0; index < 100; index++)
            {
                Assert.AreEqual(
                    expected,
                    RngStateFingerprint.FromBytes("codec-v1", bytes));
            }

            Assert.AreEqual(64, expected.Sha256.Length);
        }

        [TestMethod]
        public void Constructor_RejectsRuntimeHashCodeAndMalformedHashes()
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new RngStateFingerprint(
                    "codec-v1",
                    "123456"));
            Assert.ThrowsExactly<ArgumentException>(
                () => new RngStateFingerprint(
                    "codec-v1",
                    new string('A', 64)));
        }
    }
}
