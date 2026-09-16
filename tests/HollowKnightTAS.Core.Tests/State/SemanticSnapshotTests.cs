using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.State;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.State
{
    [TestClass]
    public sealed class SemanticSnapshotTests
    {
        private const string GoldenSha256 =
            "d41218d92af9d54d613d12232860d076e475bed68277f2631a5e35a76f76685d";

        [TestMethod]
        public void GoldenFixture_RoundTripsWithStableBytesAndHash()
        {
            var fixture = ReadFixture("golden-v1.snapshot.hex");
            var snapshot = SemanticSnapshotCanonicalizer.Deserialize(fixture);

            CollectionAssert.AreEqual(
                fixture,
                SemanticSnapshotCanonicalizer.Serialize(snapshot));
            Assert.AreEqual(GoldenSha256, SemanticSnapshotHasher.ComputeSha256(snapshot));
            Assert.AreEqual(16, snapshot.Values.Count);
            Assert.AreEqual(
                "GG_Workshop",
                snapshot.Values["scene.name"].DisplayValue);
        }

        [TestMethod]
        public void InsertionOrderAndCulture_DoNotChangeCanonicalBytesOrHash()
        {
            var forward = CreateSnapshot(
                SemanticSnapshotSchemaV1.Keys,
                0x41480000);
            var reverse = CreateSnapshot(
                SemanticSnapshotSchemaV1.Keys.Reverse(),
                0x41480000);
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar-SA");

                CollectionAssert.AreEqual(
                    SemanticSnapshotCanonicalizer.Serialize(forward),
                    SemanticSnapshotCanonicalizer.Serialize(reverse));
                Assert.AreEqual(
                    SemanticSnapshotHasher.ComputeSha256(forward),
                    SemanticSnapshotHasher.ComputeSha256(reverse));
                Assert.AreEqual(GoldenSha256, SemanticSnapshotHasher.ComputeSha256(reverse));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        [TestMethod]
        public void FloatBits_DistinguishSignedZeroNanPayloadAndOneBitChange()
        {
            AssertFloatDifference(0x00000000, unchecked((int)0x80000000));
            AssertFloatDifference(0x7fc00001, 0x7fc00002);
            AssertFloatDifference(0x3f800000, 0x3f800001);
        }

        [TestMethod]
        public void ChangedFixture_ReportsExactlyHeroXWithTypeBitsAndDisplay()
        {
            var expected = SemanticSnapshotCanonicalizer.Deserialize(
                ReadFixture("golden-v1.snapshot.hex"));
            var actual = SemanticSnapshotCanonicalizer.Deserialize(
                ReadFixture("changed-hero-x.snapshot.hex"));

            var diff = SemanticSnapshotDiffer.Compare(expected, actual);

            Assert.IsFalse(diff.AreEqual);
            Assert.HasCount(1, diff.Entries);
            var entry = diff.FirstDifference!;
            Assert.AreEqual("hero.position.x", entry.Key);
            Assert.AreEqual(SemanticValueKind.Float32Bits, entry.ExpectedKind);
            Assert.AreEqual(SemanticValueKind.Float32Bits, entry.ActualKind);
            Assert.AreEqual("41480000", entry.ExpectedBits);
            Assert.AreEqual("414c0000", entry.ActualBits);
            Assert.AreEqual("12.5", entry.ExpectedDisplay);
            Assert.AreEqual("12.75", entry.ActualDisplay);
        }

        [TestMethod]
        public void BuilderRejectsMissingDuplicateUnknownAndWrongType()
        {
            var missing = new SemanticSnapshotBuilder();
            missing.AddString("scene.name", "GG_Workshop");
            Assert.ThrowsExactly<InvalidOperationException>(() => missing.Build());

            var duplicate = new SemanticSnapshotBuilder();
            duplicate.AddString("scene.name", "GG_Workshop");
            Assert.ThrowsExactly<InvalidOperationException>(
                () => duplicate.AddString("scene.name", "GG_Workshop"));

            var unknown = new SemanticSnapshotBuilder();
            Assert.ThrowsExactly<ArgumentException>(
                () => unknown.AddInt32("runtime.private", 1));

            var wrongType = new SemanticSnapshotBuilder();
            Assert.ThrowsExactly<ArgumentException>(
                () => wrongType.AddBoolean("scene.name", true));
        }

        [TestMethod]
        public void CanonicalDecoderRejectsTrailingTruncatedAndNonCanonicalBoolean()
        {
            var fixture = ReadFixture("golden-v1.snapshot.hex");
            var trailing = fixture.Concat(new byte[] { 0 }).ToArray();
            var truncated = fixture.Take(fixture.Length - 1).ToArray();
            var invalidBoolean = (byte[])fixture.Clone();
            var booleanValueOffset = FindSequence(
                invalidBoolean,
                new byte[]
                {
                    0x68, 0x65, 0x72, 0x6f, 0x2e, 0x63, 0x53, 0x74, 0x61, 0x74,
                    0x65, 0x2e, 0x61, 0x74, 0x74, 0x61, 0x63, 0x6b, 0x69, 0x6e,
                    0x67, 0x01, 0, 0, 0, 1, 0
                });
            Assert.IsTrue(booleanValueOffset >= 0);
            invalidBoolean[booleanValueOffset + 26] = 2;

            Assert.ThrowsExactly<InvalidDataException>(
                () => SemanticSnapshotCanonicalizer.Deserialize(trailing));
            Assert.ThrowsExactly<InvalidDataException>(
                () => SemanticSnapshotCanonicalizer.Deserialize(truncated));
            Assert.ThrowsExactly<InvalidDataException>(
                () => SemanticSnapshotCanonicalizer.Deserialize(invalidBoolean));
        }

        [TestMethod]
        public void CanonicalBytesPropertyIsDefensiveAndDefaultValueIsRejected()
        {
            var value = SemanticValue.FromInt32(7);
            var bytes = value.CanonicalBytes;
            bytes[3] = 8;

            Assert.AreEqual("00000007", value.CanonicalHex);
            Assert.ThrowsExactly<InvalidOperationException>(
                () => default(SemanticValue).GetCanonicalBytes());
            Assert.ThrowsExactly<ArgumentNullException>(
                () => SemanticValue.FromString(null!));
            Assert.ThrowsExactly<EncoderFallbackException>(
                () => SemanticValue.FromString("\ud800"));
        }

        [TestMethod]
        public void MovieV1DefaultRegistryContainsEverySnapshotPath()
        {
            var registered = MovieProtocolV1.DefaultSemanticPaths;
            foreach (var key in SemanticSnapshotSchemaV1.Keys)
            {
                Assert.IsTrue(registered.Contains(key), "Missing movie path " + key);
            }
        }

        private static void AssertFloatDifference(int expectedBits, int actualBits)
        {
            var expected = CreateSnapshot(
                SemanticSnapshotSchemaV1.Keys,
                expectedBits);
            var actual = CreateSnapshot(
                SemanticSnapshotSchemaV1.Keys,
                actualBits);

            Assert.AreNotEqual(
                SemanticSnapshotHasher.ComputeSha256(expected),
                SemanticSnapshotHasher.ComputeSha256(actual));
            var diff = SemanticSnapshotDiffer.Compare(expected, actual);
            Assert.HasCount(1, diff.Entries);
            Assert.AreEqual("hero.position.x", diff.FirstDifference!.Key);
            Assert.AreNotEqual(
                diff.FirstDifference.ExpectedBits,
                diff.FirstDifference.ActualBits);
        }

        private static SemanticSnapshot CreateSnapshot(
            IEnumerable<string> order,
            int heroXBits)
        {
            var builder = new SemanticSnapshotBuilder();
            foreach (var key in order)
            {
                switch (key)
                {
                    case "game.state":
                        builder.AddString(key, "PLAYING");
                        break;
                    case "hero.actorState":
                        builder.AddString(key, "IDLE");
                        break;
                    case "hero.cState.onGround":
                        builder.AddBoolean(key, true);
                        break;
                    case "hero.cState.attacking":
                    case "hero.cState.dashing":
                    case "hero.cState.falling":
                    case "hero.cState.jumping":
                    case "hero.cState.wallSliding":
                        builder.AddBoolean(key, false);
                        break;
                    case "hero.position.x":
                        builder.AddFloat32Bits(key, heroXBits);
                        break;
                    case "hero.position.y":
                        builder.AddFloat32Bits(key, unchecked((int)0xc0500000));
                        break;
                    case "hero.velocity.x":
                        builder.AddFloat32Bits(key, 0);
                        break;
                    case "hero.velocity.y":
                        builder.AddFloat32Bits(key, unchecked((int)0x80000000));
                        break;
                    case "player.health":
                        builder.AddInt32(key, 5);
                        break;
                    case "player.maxHealth":
                        builder.AddInt32(key, 9);
                        break;
                    case "player.mp":
                        builder.AddInt32(key, 33);
                        break;
                    case "scene.name":
                        builder.AddString(key, "GG_Workshop");
                        break;
                    default:
                        Assert.Fail("Unhandled semantic key " + key);
                        break;
                }
            }

            return builder.Build();
        }

        private static byte[] ReadFixture(string name)
        {
            var path = Path.Combine(
                AppContext.BaseDirectory,
                "fixtures",
                "state",
                name);
            var hex = File.ReadAllText(path).Trim();
            return Convert.FromHexString(hex);
        }

        private static int FindSequence(byte[] source, byte[] pattern)
        {
            for (var offset = 0; offset <= source.Length - pattern.Length; offset++)
            {
                var matches = true;
                for (var index = 0; index < pattern.Length; index++)
                {
                    if (source[offset + index] != pattern[index])
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return offset;
                }
            }

            return -1;
        }
    }
}
