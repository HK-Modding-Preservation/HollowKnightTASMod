using HollowKnightTAS.Runtime.Manifest;
using HollowKnightTAS.Runtime.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace HollowKnightTAS.Core.Tests.Manifest
{
    [TestClass]
    public sealed class ModCompatibilityPolicyTests
    {
        [TestMethod]
        [DataRow(true, "[\"HollowKnightTAS\"]")]
        [DataRow(true, "[\"AnUnknownMod\",\"AnotherMod\"]")]
        [DataRow(false, "[]")]
        [DataRow(false, "null")]
        public void LegacyModAllowlistIsIgnoredWithoutChangingVerificationIntent(bool requested, string legacyList)
        {
            var oldJson = "{\"VerificationModeRequested\":" + (requested ? "true" : "false")
                + ",\"AllowedVerificationMods\":" + legacyList + "}";
            var settings = JsonConvert.DeserializeObject<TasGlobalSettings>(oldJson)!.CloneNormalized();
            var expected = new TasGlobalSettings { VerificationModeRequested = requested };
            Assert.AreEqual(expected.ComputeCanonicalSha256(), settings.ComputeCanonicalSha256());
            var preflight = VerificationPreflight.Evaluate(settings.VerificationModeRequested);
            Assert.AreEqual(requested, preflight.Requested);
            Assert.AreEqual(requested, preflight.Allowed);
            Assert.AreEqual(0, preflight.UnexpectedMods.Count);
            Assert.IsFalse(JsonConvert.SerializeObject(settings).Contains("AllowedVerificationMods"));
        }
    }
}
