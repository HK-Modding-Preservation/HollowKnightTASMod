using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Deployment;
using HollowKnightTAS.Core.Ipc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Deployment
{
    [TestClass]
    public sealed class CompanionBundleTests
    {
        [TestMethod]
        public void ManifestRoundTripIsCanonical()
        {
            using (var rsa = RSA.Create(2048))
            {
                var manifest = CreateManifest(
                    "00".PadRight(64, '0'));
                manifest = manifest.WithSignature(
                    CompanionBundleVerifier.Sign(manifest, rsa));
                var bytes = CompanionManifestCodec.Serialize(manifest);
                var decoded =
                    CompanionManifestCodec.TryDeserialize(bytes);

                Assert.IsTrue(decoded.Success, decoded.Error);
                Assert.AreEqual(
                    "Companion/win-x64/HollowKnightTAS.Companion.exe",
                    decoded.Manifest!.Entrypoint);
                CollectionAssert.AreEqual(
                    bytes,
                    CompanionManifestCodec.Serialize(
                        decoded.Manifest));
            }
        }

        [TestMethod]
        public void ValidBundleVerifiesSignatureHashRidAndProtocol()
        {
            WithBundle(
                context =>
                {
                    var result = CompanionBundleVerifier.Verify(
                        context.Root,
                        context.ManifestPath,
                        "win-x64",
                        new ProtocolRange(1, 1),
                        context.PublicKey);

                    Assert.IsTrue(result.Success, result.Detail);
                    Assert.AreEqual(
                        context.EntrypointPath,
                        result.EntrypointPath);
                    Assert.AreEqual(1, result.VerifiedFiles.Count);
                });
        }

        [TestMethod]
        public void TamperedFileFailsClosed()
        {
            WithBundle(
                context =>
                {
                    File.AppendAllText(
                        context.EntrypointPath,
                        "tampered",
                        Encoding.UTF8);
                    var result = context.Verify();
                    Assert.AreEqual(
                        CompanionBundleStatus.FileHashMismatch,
                        result.Status);
                });
        }

        [TestMethod]
        public void MissingAndTruncatedEntrypointFailClosed()
        {
            WithBundle(
                context =>
                {
                    File.Delete(context.EntrypointPath);
                    Assert.AreEqual(
                        CompanionBundleStatus.FileMissing,
                        context.Verify().Status);
                });
            WithBundle(
                context =>
                {
                    File.WriteAllBytes(
                        context.EntrypointPath,
                        new byte[] { 0x4d, 0x5a });
                    Assert.AreEqual(
                        CompanionBundleStatus.FileHashMismatch,
                        context.Verify().Status);
                });
        }

        [TestMethod]
        public void TamperedSignatureFailsBeforeFileHash()
        {
            WithBundle(
                context =>
                {
                    var bytes = File.ReadAllBytes(
                        context.ManifestPath);
                    var decode =
                        CompanionManifestCodec.TryDeserialize(bytes);
                    var signature = Convert.FromBase64String(
                        decode.Manifest!.Signature);
                    signature[0] ^= 1;
                    var invalid = decode.Manifest.WithSignature(
                        Convert.ToBase64String(signature));
                    File.WriteAllBytes(
                        context.ManifestPath,
                        CompanionManifestCodec.Serialize(invalid));
                    File.AppendAllText(
                        context.EntrypointPath,
                        "also-tampered",
                        Encoding.UTF8);

                    var result = context.Verify();
                    Assert.AreEqual(
                        CompanionBundleStatus.SignatureInvalid,
                        result.Status);
                });
        }

        [TestMethod]
        public void RidAndProtocolMismatchFailClosed()
        {
            WithBundle(
                context =>
                {
                    Assert.AreEqual(
                        CompanionBundleStatus.RidMismatch,
                        CompanionBundleVerifier.Verify(
                            context.Root,
                            context.ManifestPath,
                            "linux-x64",
                            new ProtocolRange(1, 1),
                            context.PublicKey).Status);
                    Assert.AreEqual(
                        CompanionBundleStatus.ProtocolMismatch,
                        CompanionBundleVerifier.Verify(
                            context.Root,
                            context.ManifestPath,
                            "win-x64",
                            new ProtocolRange(2, 2),
                            context.PublicKey).Status);
                });
        }

        [TestMethod]
        public void RelativePathPolicyRejectsEscapeAndAmbiguity()
        {
            var root = Path.GetTempPath();
            foreach (var path in new[]
                     {
                         "../outside.exe",
                         "Companion/../outside.exe",
                         "C:/outside.exe",
                         "/outside.exe",
                         "Companion\\tool.exe",
                         "Companion//tool.exe",
                         "Companion/./tool.exe"
                     })
            {
                Assert.IsFalse(
                    CompanionPathPolicy.TryResolve(
                        root,
                        path,
                        out _,
                        out _),
                    path);
            }

            Assert.IsTrue(
                CompanionPathPolicy.TryResolve(
                    root,
                    "Companion/win-x64/tool.exe",
                    out var resolved,
                    out var error),
                error);
            StringAssert.StartsWith(
                resolved!,
                Path.GetFullPath(root),
                StringComparison.OrdinalIgnoreCase);
        }

        [TestMethod]
        public void EntrypointMustBeDeclared()
        {
            WithBundle(
                context =>
                {
                    using (var rsa = context.PrivateKey)
                    {
                        var decoded =
                            CompanionManifestCodec.TryDeserialize(
                                File.ReadAllBytes(
                                    context.ManifestPath));
                        var manifest = new CompanionBundleManifest(
                            1,
                            CompanionManifestCodec.Product,
                            "0.1.0",
                            "win-x64",
                            1,
                            1,
                            "Companion/win-x64/other.exe",
                            decoded.Manifest!.Files,
                            string.Empty);
                        manifest = manifest.WithSignature(
                            CompanionBundleVerifier.Sign(
                                manifest,
                                rsa));
                        File.WriteAllBytes(
                            context.ManifestPath,
                            CompanionManifestCodec.Serialize(
                                manifest));
                    }

                    Assert.AreEqual(
                        CompanionBundleStatus.EntrypointMissing,
                        context.Verify().Status);
                },
                disposePrivateKey: false);
        }

        [TestMethod]
        public void LaunchBackoffUsesFixedScheduleAndFiveMinuteCircuit()
        {
            var guard = new CompanionLaunchBackoff();
            var now = new DateTimeOffset(
                2026,
                7,
                29,
                0,
                0,
                0,
                TimeSpan.Zero);
            var expected = new[] { 1d, 2d, 5d, 15d, 30d };
            for (var index = 0; index < expected.Length; index++)
            {
                var result = guard.RegisterFailure(
                    now.AddSeconds(index));
                Assert.AreEqual(
                    expected[index],
                    result.Delay.TotalSeconds);
                Assert.AreEqual(index == 4, result.CircuitOpen);
            }

            guard.Reset();
            Assert.IsFalse(
                guard.RegisterFailure(now.AddMinutes(10))
                    .CircuitOpen);
        }

        [TestMethod]
        public void ValidBundleSupportsUnicodeAndSpacesInFixedRoot()
        {
            WithBundle(
                context =>
                {
                    var result = context.Verify();
                    Assert.IsTrue(result.Success, result.Detail);
                },
                directoryPrefix: "空洞 骑士 Companion ");
        }

        [TestMethod]
        public void ReparsePointTraversalFailsClosed()
        {
            WithBundle(
                context =>
                {
                    var companion = Path.Combine(
                        context.Root,
                        "Companion");
                    var target = context.Root
                                 + ".reparse-target";
                    Directory.Move(companion, target);
                    try
                    {
                        try
                        {
                            Directory.CreateSymbolicLink(
                                companion,
                                target);
                        }
                        catch (Exception exception)
                            when (exception
                                      is UnauthorizedAccessException
                                  || exception is IOException
                                  || exception
                                      is PlatformNotSupportedException)
                        {
                            Assert.Inconclusive(
                                "Symbolic links are unavailable: "
                                + exception.Message);
                        }

                        Assert.AreEqual(
                            CompanionBundleStatus
                                .ReparsePointRejected,
                            context.Verify().Status);
                    }
                    finally
                    {
                        if (Directory.Exists(companion)
                            && (File.GetAttributes(companion)
                                & FileAttributes.ReparsePoint) != 0)
                        {
                            Directory.Delete(companion);
                        }

                        if (Directory.Exists(target)
                            && !Directory.Exists(companion))
                        {
                            Directory.Move(target, companion);
                        }
                    }
                });
        }

        private static CompanionBundleManifest CreateManifest(
            string sha256)
        {
            return new CompanionBundleManifest(
                1,
                CompanionManifestCodec.Product,
                "0.1.0",
                "win-x64",
                1,
                1,
                "Companion/win-x64/HollowKnightTAS.Companion.exe",
                new[]
                {
                    new CompanionManifestFile(
                        "Companion/win-x64/HollowKnightTAS.Companion.exe",
                        sha256)
                },
                string.Empty);
        }

        private static void WithBundle(
            Action<BundleContext> action,
            bool disposePrivateKey = true,
            string directoryPrefix =
                "hktas-bundle-test-")
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                directoryPrefix
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(
                Path.Combine(root, "Companion", "win-x64"));
            var entrypoint = Path.Combine(
                root,
                "Companion",
                "win-x64",
                "HollowKnightTAS.Companion.exe");
            File.WriteAllBytes(
                entrypoint,
                Encoding.UTF8.GetBytes("test companion"));
            var rsa = RSA.Create(2048);
            try
            {
                var manifest = CreateManifest(
                    Sha256Utility.ComputeFileHex(entrypoint));
                manifest = manifest.WithSignature(
                    CompanionBundleVerifier.Sign(manifest, rsa));
                var manifestPath = Path.Combine(
                    root,
                    "companion.manifest.json");
                File.WriteAllBytes(
                    manifestPath,
                    CompanionManifestCodec.Serialize(manifest));
                var publicParameters = rsa.ExportParameters(false);
                var context = new BundleContext(
                    root,
                    manifestPath,
                    entrypoint,
                    rsa,
                    new RsaPublicKey(
                        publicParameters.Modulus!,
                        publicParameters.Exponent!));
                action(context);
            }
            finally
            {
                if (disposePrivateKey)
                {
                    rsa.Dispose();
                }

                Directory.Delete(root, true);
            }
        }

        private sealed class BundleContext
        {
            public BundleContext(
                string root,
                string manifestPath,
                string entrypointPath,
                RSA privateKey,
                RsaPublicKey publicKey)
            {
                Root = root;
                ManifestPath = manifestPath;
                EntrypointPath = entrypointPath;
                PrivateKey = privateKey;
                PublicKey = publicKey;
            }

            public string Root { get; }
            public string ManifestPath { get; }
            public string EntrypointPath { get; }
            public RSA PrivateKey { get; }
            public RsaPublicKey PublicKey { get; }

            public CompanionBundleVerificationResult Verify()
            {
                return CompanionBundleVerifier.Verify(
                    Root,
                    ManifestPath,
                    "win-x64",
                    new ProtocolRange(1, 1),
                    PublicKey);
            }
        }
    }
}
