using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using HollowKnightTAS.Core.Manifest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Manifest
{
    [TestClass]
    public sealed class ManifestCanonicalizerTests
    {
        [TestMethod]
        public void Serialize_IsStableAcrossDictionaryInsertionOrder()
        {
            var first = CreateManifest(
                new Dictionary<string, string>
                {
                    ["z.dll"] = Repeat('f'),
                    ["a.dll"] = Repeat('0')
                },
                new Dictionary<string, string>
                {
                    ["Zulu"] = "2",
                    ["Alpha"] = "1"
                });
            var second = CreateManifest(
                new Dictionary<string, string>
                {
                    ["a.dll"] = Repeat('0'),
                    ["z.dll"] = Repeat('f')
                },
                new Dictionary<string, string>
                {
                    ["Alpha"] = "1",
                    ["Zulu"] = "2"
                });

            CollectionAssert.AreEqual(
                ManifestCanonicalizer.Serialize(first),
                ManifestCanonicalizer.Serialize(second));
            Assert.AreEqual(
                ManifestCanonicalizer.ComputeSha256(first),
                ManifestCanonicalizer.ComputeSha256(second));
        }

        [TestMethod]
        public void Serialize_RepeatedOneHundredTimesIsByteIdentical()
        {
            var manifest = CreateManifest(
                new Dictionary<string, string> { ["Assembly-CSharp.dll"] = Repeat('a') },
                new Dictionary<string, string> { ["HollowKnightTAS"] = "0.1.0" });
            var expected = ManifestCanonicalizer.Serialize(manifest);
            var expectedHash = ManifestCanonicalizer.ComputeSha256(manifest);

            for (var run = 0; run < 100; run++)
            {
                CollectionAssert.AreEqual(expected, ManifestCanonicalizer.Serialize(manifest));
                Assert.AreEqual(expectedHash, ManifestCanonicalizer.ComputeSha256(manifest));
            }
        }

        [TestMethod]
        public void Serialize_ProducesCanonicalUtf8WithRequiredFields()
        {
            var manifest = CreateManifest(
                new Dictionary<string, string> { ["Assembly-CSharp.dll"] = Repeat('a') },
                new Dictionary<string, string> { ["HollowKnightTAS"] = "0.1.0" });
            var bytes = ManifestCanonicalizer.Serialize(manifest);

            Assert.IsFalse(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
            var json = new UTF8Encoding(false, true).GetString(bytes);
            Assert.IsFalse(json.Contains("\r", StringComparison.Ordinal));
            Assert.IsFalse(json.Contains("\n", StringComparison.Ordinal));
            Assert.IsTrue(json.IndexOf("\"assemblySha256\"", StringComparison.Ordinal)
                          < json.IndexOf("\"loadedMods\"", StringComparison.Ordinal));

            using var parsed = JsonDocument.Parse(bytes);
            Assert.AreEqual("1.5.78.11833", parsed.RootElement.GetProperty("gameVersion").GetString());
            Assert.AreEqual(1920, parsed.RootElement.GetProperty("screenWidth").GetInt32());
            Assert.IsFalse(parsed.RootElement.GetProperty("verificationModeAllowed").GetBoolean());
        }

        [TestMethod]
        public void Constructor_SortsAndDeduplicatesUnexpectedMods()
        {
            var manifest = new EnvironmentManifest(
                1,
                "1.5.78.11833",
                "1.5.78.11833-77",
                new Dictionary<string, string> { ["a"] = "b" },
                new Dictionary<string, string>(),
                Repeat('1'),
                "none",
                "none",
                "Windows",
                "x64",
                "zh-CN",
                "ZH",
                2,
                1920,
                1080,
                "FullScreenWindow",
                60,
                true,
                true,
                false,
                new[] { "Zulu", "Alpha", "Zulu" });

            CollectionAssert.AreEqual(
                new[] { "Alpha", "Zulu" },
                manifest.UnexpectedMods.ToArray());
        }

        [TestMethod]
        public void SchemaTwo_SerializesRngCapability()
        {
            var manifest = new EnvironmentManifest(
                2,
                "1.5.78.11833",
                "1.5.78.11833-77",
                new Dictionary<string, string> { ["a"] = "b" },
                new Dictionary<string, string>(),
                Repeat('1'),
                "none",
                "none",
                "Windows",
                "x64",
                "zh-CN",
                "ZH",
                2,
                1920,
                1080,
                "FullScreenWindow",
                50,
                true,
                false,
                false,
                Array.Empty<string>(),
                "codec-v1",
                "partial-v1");

            using var parsed = JsonDocument.Parse(
                ManifestCanonicalizer.Serialize(manifest));

            Assert.AreEqual(
                "codec-v1",
                parsed.RootElement.GetProperty("rngCodecId").GetString());
            Assert.AreEqual(
                "partial-v1",
                parsed.RootElement.GetProperty("rngCoverage").GetString());
        }

        private static EnvironmentManifest CreateManifest(
            IReadOnlyDictionary<string, string> assemblies,
            IReadOnlyDictionary<string, string> mods)
        {
            return new EnvironmentManifest(
                1,
                "1.5.78.11833",
                "1.5.78.11833-77",
                assemblies,
                mods,
                Repeat('1'),
                "none",
                "none",
                "Microsoft Windows NT 10.0",
                "x64",
                "zh-CN",
                "ZH",
                2,
                1920,
                1080,
                "FullScreenWindow",
                60,
                true,
                true,
                false,
                new[] { "UnexpectedB", "UnexpectedA" });
        }

        private static string Repeat(char value)
        {
            return new string(value, 64);
        }
    }
}
