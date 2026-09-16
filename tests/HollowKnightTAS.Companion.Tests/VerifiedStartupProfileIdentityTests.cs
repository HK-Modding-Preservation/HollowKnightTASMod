using System;
using System.IO;
using System.Text.Json;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Ipc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class VerifiedStartupProfileIdentityTests
    {
        [TestMethod]
        [DataRow("profile")]
        [DataRow("policy")]
        [DataRow("seed")]
        [DataRow("current")]
        public void IdentityRejectsMixedScenePoliciesBeforeRuntimeFileValidation(string variant)
        {
            var root = Path.Combine(Path.GetTempPath(), "hktas-profile-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var game = Path.Combine(root, "hollow_knight.exe");
                File.WriteAllBytes(game, Array.Empty<byte>());
                File.WriteAllText(Path.Combine(root, "clock-build-whitelist-v1.json"), "{}");
                File.WriteAllText(Path.Combine(root, "clock-build-manifest-v1.json"), JsonSerializer.Serialize(new
                {
                    schemaVersion = 2,
                    capabilityId = VerifiedStartupProfile.CapabilityId,
                    profile = variant == "profile"
                        ? "external-unity-startup-continuous-clock-v39-post-root-request-first"
                        : StartupProfileContract.ProfileId,
                    startupPolicy = VerifiedStartupProfile.StartupPolicy,
                    bridgeAbi = StartupProfileContract.BridgeAbi,
                    startupFrameGateAbi = 1,
                    randomSynchronizationPolicy = variant == "policy"
                        ? "unity-init-state-at-root-post-root-request-first-and-activation-finish-aligned-scene-boundaries-v18"
                        : StartupProfileContract.RandomSynchronizationPolicyId,
                    randomSynchronizationSeed = StartupProfileContract.RandomSynchronizationSeed + (variant == "seed" ? 1 : 0)
                    // Deliberately omit game metadata: current identity must get
                    // past identity validation, not make this fixture launchable.
                }));
                var exception = Assert.ThrowsExactly<InvalidDataException>(() => VerifiedStartupProfile.Load(root, game));
                if (variant == "current")
                    Assert.AreEqual("Startup profile does not declare Hollow Knight.", exception.Message);
                else
                    StringAssert.Contains(exception.Message, "required v40 native-scene profile");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
