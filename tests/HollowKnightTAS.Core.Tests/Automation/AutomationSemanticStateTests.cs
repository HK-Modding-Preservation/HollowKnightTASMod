using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HollowKnightTAS.Automation.Client;
using HollowKnightTAS.Core.Automation;
using HollowKnightTAS.Core.State;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Automation
{
    [TestClass]
    public sealed class AutomationSemanticStateTests
    {
        private const string Hash =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string SnapshotJson =
            "{\"schemaVersion\":1,\"sha256\":\""
            + Hash
            + "\",\"values\":["
            + "{\"key\":\"flag\",\"kind\":\"Boolean\",\"canonicalHex\":\"01\",\"displayValue\":\"true\"},"
            + "{\"key\":\"hero.position.x\",\"kind\":\"Float32Bits\",\"canonicalHex\":\"3fc00000\",\"displayValue\":\"1.5\"},"
            + "{\"key\":\"name\",\"kind\":\"Utf8String\",\"canonicalHex\":\"e58187e9aa91e5a3ab\",\"displayValue\":\"假骑士\"},"
            + "{\"key\":\"player.health\",\"kind\":\"Int32\",\"canonicalHex\":\"00000005\",\"displayValue\":\"5\"}"
            + "]}";

        [TestMethod]
        public void TypedSemanticViewReturnsNativeValuesAndFriendlyJson()
        {
            var semantic = AutomationSemanticSnapshot.Parse(
                SnapshotJson);
            Assert.AreEqual(Hash, semantic.Sha256);
            Assert.IsTrue(
                semantic.TryGetBoolean("flag", out var flag));
            Assert.IsTrue(flag);
            Assert.IsTrue(
                semantic.TryGetFloat32(
                    "hero.position.x",
                    out var position));
            Assert.AreEqual(1.5f, position);
            Assert.IsTrue(
                semantic.TryGetString("name", out var name));
            Assert.AreEqual("假骑士", name);
            Assert.IsTrue(
                semantic.TryGetInt32(
                    "player.health",
                    out var health));
            Assert.AreEqual(5, health);
            Assert.AreEqual(
                SemanticValueKind.Int32,
                semantic["player.health"].Kind);

            var state = CreateState(Hash, SnapshotJson);
            var view = AutomationSemanticState.Parse(
                AutomationStateCodec.Serialize(state));
            using var friendly = JsonDocument.Parse(
                view.ToFriendlyJson());
            Assert.AreEqual(
                5,
                friendly.RootElement
                    .GetProperty("semanticValues")
                    .GetProperty("player.health")
                    .GetProperty("value")
                    .GetInt32());
            Assert.AreEqual(
                1.5f,
                friendly.RootElement
                    .GetProperty("semanticValues")
                    .GetProperty("hero.position.x")
                    .GetProperty("value")
                    .GetSingle());
        }

        [TestMethod]
        public void FloatDisplayMayDifferAcrossClrImplementations()
        {
            var semantic = AutomationSemanticSnapshot.Parse(
                SnapshotJson.Replace(
                    "\"displayValue\":\"1.5\"",
                    "\"displayValue\":\"1.50000000\"",
                    StringComparison.Ordinal));

            Assert.IsTrue(
                semantic.TryGetFloat32(
                    "hero.position.x",
                    out var position));
            Assert.AreEqual(1.5f, position);
            Assert.AreEqual(
                "1.50000000",
                semantic["hero.position.x"].DisplayValue);
        }

        [TestMethod]
        public void TypedSemanticViewRejectsNonCanonicalOrMismatchedData()
        {
            Assert.ThrowsExactly<InvalidDataException>(
                () => AutomationSemanticSnapshot.Parse(
                    SnapshotJson.Replace(
                        "\"displayValue\":\"5\"",
                        "\"displayValue\":\"6\"",
                        StringComparison.Ordinal)));
            Assert.ThrowsExactly<InvalidDataException>(
                () => AutomationSemanticSnapshot.Parse(
                    SnapshotJson.Replace(
                        "\"hero.position.x\"",
                        "\"z\"",
                        StringComparison.Ordinal)));

            var state = CreateState(
                new string('f', 64),
                SnapshotJson);
            Assert.ThrowsExactly<InvalidDataException>(
                () => AutomationSemanticState.Parse(
                    AutomationStateCodec.Serialize(state)));
        }

        private static AutomationStateEnvelope CreateState(
            string snapshotHash,
            string snapshotJson)
        {
            return new AutomationStateEnvelope(
                "session-typed",
                Hash,
                "Paused",
                42,
                "LateUpdateEnd",
                DateTimeOffset.Parse(
                    "2026-07-29T06:00:00.0000000+00:00"),
                1,
                snapshotHash,
                new Dictionary<string, string>(
                    StringComparer.Ordinal)
                {
                    ["controlMode"] = "Paused",
                    ["semanticSnapshotJson"] = snapshotJson
                },
                new[] { AutomationCommandIds.GetState });
        }
    }
}
