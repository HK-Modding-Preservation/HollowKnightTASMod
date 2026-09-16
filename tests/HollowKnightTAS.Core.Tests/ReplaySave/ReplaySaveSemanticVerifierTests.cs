using System;
using HollowKnightTAS.Core.ReplaySave;
using HollowKnightTAS.Core.State;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.ReplaySave
{
    [TestClass]
    public sealed class ReplaySaveSemanticVerifierTests
    {
        [TestMethod]
        public void OneUlpFloatNoise_PreservesStrictDiffButPassesProjection()
        {
            var x = 50.79f;
            var adjacent = BitConverter.Int32BitsToSingle(
                BitConverter.SingleToInt32Bits(x) + 1);
            var comparison = ReplaySaveSemanticVerifier.Compare(
                Snapshot(x, attacking: false),
                Snapshot(adjacent, attacking: false));

            Assert.IsFalse(comparison.StrictEquivalent);
            Assert.IsTrue(comparison.VerificationEquivalent);
            Assert.AreEqual(
                "v1-float32-decimal-4",
                comparison.ProjectionId);
            Assert.AreEqual(
                comparison.ExpectedVerificationSha256,
                SemanticSnapshotHasher.ComputeSha256(
                    ReplaySaveSemanticVerifier.Project(
                        Snapshot(x, attacking: false))));
        }

        [TestMethod]
        public void MaterialFloatOrBooleanDifference_FailsProjection()
        {
            var expected = Snapshot(50.79f, attacking: false);
            var moved = ReplaySaveSemanticVerifier.Compare(
                expected,
                Snapshot(50.791f, attacking: false));
            var changedState = ReplaySaveSemanticVerifier.Compare(
                expected,
                Snapshot(50.79f, attacking: true));

            Assert.IsFalse(moved.VerificationEquivalent);
            Assert.IsFalse(changedState.VerificationEquivalent);
        }

        [TestMethod]
        public void ReplayTargetV2_PreservesRngThroughCanonicalRoundTripAndProjection()
        {
            var expected = Snapshot(38.2840157f, false,
                "4ebed9e38296abab3c601a238be6dfe71b1654a3a33b2f1519e7c0eeaa209682");
            var roundTrip = SemanticSnapshotCanonicalizer.Deserialize(
                SemanticSnapshotCanonicalizer.Serialize(expected));
            Assert.AreEqual(2, roundTrip.SchemaVersion);
            Assert.IsTrue(ReplaySaveSemanticVerifier.Compare(expected, roundTrip).StrictEquivalent);
            foreach (var actual in new[]
            {
                Snapshot(38.2840157f, false,
                    "4e07f883fb1839b21403e2b34bb3f8bc6c848c2cd5037317c55a1ec7fb9879b9"),
                Snapshot(38.2840157f, false)
            })
            {
                var comparison = ReplaySaveSemanticVerifier.Compare(expected, actual);
                Assert.IsFalse(comparison.StrictEquivalent);
                Assert.IsFalse(comparison.VerificationEquivalent);
            }
        }

        [TestMethod]
        public void BaselineV1_StillRejectsRandomExtensionAndV2RequiresIt()
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                new SemanticSnapshotBuilder().AddString("rng.state.sha256", "test"));
            Assert.ThrowsExactly<ArgumentException>(() =>
                new SemanticSnapshotBuilder(2).AddInt32("rng.state.sha256", 1));
            var canonical = SemanticSnapshotCanonicalizer.Serialize(Snapshot(1, false));
            // The version is big endian after the four-byte magic. A v1 payload
            // cannot masquerade as v2 while omitting its required RNG entry.
            canonical[7] = 2;
            Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
                SemanticSnapshotCanonicalizer.Deserialize(canonical));
        }

        [TestMethod]
        public void ReplayTargetV3_RequiresActorsAndDetectsActorMismatch()
        {
            var expected = Snapshot(1, false, new string('a', 64), new string('b', 64));
            var roundTrip = SemanticSnapshotCanonicalizer.Deserialize(SemanticSnapshotCanonicalizer.Serialize(expected));
            Assert.AreEqual(3, roundTrip.SchemaVersion);
            Assert.IsTrue(ReplaySaveSemanticVerifier.Compare(expected, roundTrip).StrictEquivalent);
            var changed = Snapshot(1, false, new string('a', 64), new string('c', 64));
            Assert.IsFalse(ReplaySaveSemanticVerifier.Compare(expected, changed).StrictEquivalent);
            Assert.IsFalse(ReplaySaveSemanticVerifier.Compare(expected, changed).VerificationEquivalent);
            var old = Snapshot(1, false, new string('a', 64));
            Assert.IsFalse(ReplaySaveSemanticVerifier.Compare(expected, old).VerificationEquivalent);
            var bytes = SemanticSnapshotCanonicalizer.Serialize(old);
            bytes[7] = 3;
            Assert.ThrowsExactly<System.IO.InvalidDataException>(() => SemanticSnapshotCanonicalizer.Deserialize(bytes));
        }

        private static SemanticSnapshot Snapshot(float x, bool attacking, string? rng = null, string? actors = null)
        {
            var builder = new SemanticSnapshotBuilder(actors != null ? 3 : rng == null ? 1 : 2);
            builder.AddString("game.state", "PLAYING");
            builder.AddString("hero.actorState", "IDLE");
            builder.AddBoolean("hero.cState.attacking", attacking);
            builder.AddBoolean("hero.cState.dashing", false);
            builder.AddBoolean("hero.cState.falling", false);
            builder.AddBoolean("hero.cState.jumping", false);
            builder.AddBoolean("hero.cState.onGround", true);
            builder.AddBoolean("hero.cState.wallSliding", false);
            builder.AddFloat32("hero.position.x", x);
            builder.AddFloat32("hero.position.y", 13.408123f);
            builder.AddFloat32("hero.velocity.x", 0f);
            builder.AddFloat32("hero.velocity.y", 0f);
            builder.AddInt32("player.health", 5);
            builder.AddInt32("player.maxHealth", 9);
            builder.AddInt32("player.mp", 33);
            builder.AddString("scene.name", "GG_Vengefly");
            if (rng != null) builder.AddString("rng.state.sha256", rng);
            if (actors != null) builder.AddString("scene.activeHealthActors.sha256", actors);
            return builder.Build();
        }
    }
}
