using HollowKnightTAS.Runtime.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.ReplaySave
{
    [TestClass]
    public sealed class AutoSaveSettingsIdentityTests
    {
        [TestMethod]
        public void AutoSavePreferencesDoNotChangeExecutionIdentity()
        {
            var settings = new TasGlobalSettings();
            var original = settings.ComputeCanonicalSha256();
            settings.ReplaySaveAutoEnabled = !settings.ReplaySaveAutoEnabled;
            settings.ReplaySaveAutoIntervalMovieTicks = 73;
            settings.ReplaySaveAutoRetentionCount = 37;
            Assert.AreEqual(original, settings.ComputeCanonicalSha256());
            var nextLaunch = settings.CloneNormalized();
            Assert.AreEqual(settings.ReplaySaveAutoEnabled, nextLaunch.ReplaySaveAutoEnabled);
            Assert.AreEqual(73L, nextLaunch.ReplaySaveAutoIntervalMovieTicks);
            Assert.AreEqual(37, nextLaunch.ReplaySaveAutoRetentionCount);
            nextLaunch.ReplaySaveDeterministicTimingEnabled = !nextLaunch.ReplaySaveDeterministicTimingEnabled;
            Assert.AreNotEqual(original, nextLaunch.ComputeCanonicalSha256());
        }
    }
}
