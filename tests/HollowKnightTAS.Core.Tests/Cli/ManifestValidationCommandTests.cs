using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HollowKnightTAS.Cli;
using HollowKnightTAS.Core.Manifest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Cli
{
    [TestClass]
    public sealed class ManifestValidationCommandTests
    {
        [TestMethod]
        public void Run_WithMalformedFixture_ReturnsValidationErrorWithoutThrowing()
        {
            var fixture = Path.Combine(
                AppContext.BaseDirectory,
                "fixtures",
                "manifest",
                "malformed-missing-game-version.json");
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = Program.Run(
                new[] { "manifest", "validate", fixture },
                output,
                error);

            Assert.AreEqual(3, exitCode);
            Assert.AreEqual(string.Empty, output.ToString());
            StringAssert.StartsWith(error.ToString(), "INVALID Missing required property: gameVersion");
            Assert.AreEqual(
                1,
                error.ToString().Split(
                    new[] { Environment.NewLine },
                    StringSplitOptions.RemoveEmptyEntries).Length);
        }

        [TestMethod]
        public void Run_WithCanonicalManifestAndMatchingHash_ReturnsSuccess()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                var manifest = CreateManifest();
                var manifestPath = Path.Combine(directory, "manifest.json");
                var hash = ManifestCanonicalizer.ComputeSha256(manifest);
                File.WriteAllBytes(manifestPath, ManifestCanonicalizer.Serialize(manifest));
                File.WriteAllText(
                    Path.Combine(directory, "manifest.sha256"),
                    hash + "\n",
                    new UTF8Encoding(false));
                using var output = new StringWriter();
                using var error = new StringWriter();

                var exitCode = Program.Run(
                    new[] { "manifest", "validate", manifestPath },
                    output,
                    error);

                Assert.AreEqual(0, exitCode);
                Assert.AreEqual("VALID " + hash + Environment.NewLine, output.ToString());
                Assert.AreEqual(string.Empty, error.ToString());
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Run_WithMismatchedSiblingHash_ReturnsValidationError()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                var manifest = CreateManifest();
                var manifestPath = Path.Combine(directory, "manifest.json");
                File.WriteAllBytes(manifestPath, ManifestCanonicalizer.Serialize(manifest));
                File.WriteAllText(
                    Path.Combine(directory, "manifest.sha256"),
                    new string('0', 64),
                    new UTF8Encoding(false));
                using var output = new StringWriter();
                using var error = new StringWriter();

                var exitCode = Program.Run(
                    new[] { "manifest", "validate", manifestPath },
                    output,
                    error);

                Assert.AreEqual(3, exitCode);
                StringAssert.StartsWith(error.ToString(), "INVALID manifest.sha256");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Run_WithInvalidArguments_ReturnsUsageError()
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = Program.Run(Array.Empty<string>(), output, error);

            Assert.AreEqual(2, exitCode);
            StringAssert.StartsWith(error.ToString(), "Usage:");
        }

        private static string CreateTemporaryDirectory()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "HollowKnightTAS.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static EnvironmentManifest CreateManifest()
        {
            return new EnvironmentManifest(
                1,
                "1.5.78.11833",
                "1.5.78.11833-77",
                new Dictionary<string, string>
                {
                    ["Assembly-CSharp.dll"] = new string('a', 64)
                },
                new Dictionary<string, string>
                {
                    ["HollowKnightTAS"] = "0.1.0"
                },
                new string('1', 64),
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
                false,
                false,
                Array.Empty<string>());
        }
    }
}
