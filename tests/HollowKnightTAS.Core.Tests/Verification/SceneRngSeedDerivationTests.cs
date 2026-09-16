using System;
using HollowKnightTAS.ClockPayload;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Verification
{
    [TestClass]
    public sealed class SceneRngSeedDerivationTests
    {
        [TestMethod]
        public void DeriveIsStableAndMatchesCommittedByteContract()
        {
            var seed = SceneRngSeedDerivation.Derive(
                1212896321,
                "GG_False_Knight",
                1);

            Assert.AreEqual(unchecked((int)0xab97170bU), seed);
            Assert.AreEqual(
                seed,
                SceneRngSeedDerivation.Derive(
                    1212896321,
                    "GG_False_Knight",
                    1));
        }

        [TestMethod]
        public void DeriveBindsRootSceneAndEpoch()
        {
            var baseline = SceneRngSeedDerivation.Derive(
                1212896321,
                "GG_False_Knight",
                1);

            Assert.AreNotEqual(
                baseline,
                SceneRngSeedDerivation.Derive(
                    1212896322,
                    "GG_False_Knight",
                    1));
            Assert.AreNotEqual(
                baseline,
                SceneRngSeedDerivation.Derive(
                    1212896321,
                    "GG_Workshop",
                    1));
            Assert.AreNotEqual(
                baseline,
                SceneRngSeedDerivation.Derive(
                    1212896321,
                    "GG_False_Knight",
                    2));
        }

        [TestMethod]
        public void DeriveRejectsMissingSceneAndNonPositiveEpoch()
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => SceneRngSeedDerivation.Derive(1, string.Empty, 1));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => SceneRngSeedDerivation.Derive(1, "GG_Workshop", 0));
        }
    }
}
