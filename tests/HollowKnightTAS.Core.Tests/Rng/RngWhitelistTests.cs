using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HollowKnightTAS.Core.Rng;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Rng
{
    [TestClass]
    public sealed class RngWhitelistTests
    {
        [TestMethod]
        public void TargetBuild_ExactIdentityAndCallSitesResolve()
        {
            var whitelist = RngWhitelist.CreateTargetBuild();
            var resolution = whitelist.Resolve(
                whitelist.Assembly,
                whitelist.CallSites);

            Assert.IsTrue(resolution.Ready);
            Assert.AreEqual(
                RngWhitelistResolutionStatus.Ready,
                resolution.Status);
            Assert.AreEqual(2, resolution.ResolvedCallSites.Count);
        }

        [TestMethod]
        public void AssemblyOrMvidMismatch_FailsClosed()
        {
            var whitelist = RngWhitelist.CreateTargetBuild();
            var wrongAssembly = new RngAssemblyIdentity(
                whitelist.Assembly.AssemblyName,
                new string('0', 64),
                whitelist.Assembly.ModuleVersionId);
            var wrongModule = new RngAssemblyIdentity(
                whitelist.Assembly.AssemblyName,
                whitelist.Assembly.Sha256,
                Guid.Empty.ToString("D"));

            Assert.AreEqual(
                RngWhitelistResolutionStatus.AssemblyMismatch,
                whitelist.Resolve(
                    wrongAssembly,
                    whitelist.CallSites).Status);
            Assert.AreEqual(
                RngWhitelistResolutionStatus.ModuleMismatch,
                whitelist.Resolve(
                    wrongModule,
                    whitelist.CallSites).Status);
        }

        [TestMethod]
        public void MutatedTokenFixture_FailsCallSiteResolution()
        {
            var whitelist = RngWhitelist.CreateTargetBuild();
            var path = Path.Combine(
                AppContext.BaseDirectory,
                "fixtures",
                "rng",
                "whitelist-mutated-token.json");
            using var document = JsonDocument.Parse(
                File.ReadAllBytes(path));
            var value = document.RootElement.GetProperty(
                "mutatedCallSite");
            var mutated = new RngCallSiteDescriptor(
                value.GetProperty("callSiteId").GetString()!,
                value.GetProperty("declaringType").GetString()!,
                value.GetProperty("methodSignature").GetString()!,
                value.GetProperty("metadataToken").GetInt32(),
                value.GetProperty("methodIlSha256").GetString()!,
                value.GetProperty("hookType").GetString()!,
                value.GetProperty("expectedRandomApi").GetString()!,
                value.GetProperty("expectedRandomCallCount").GetInt32());
            var actual = new List<RngCallSiteDescriptor>(
                whitelist.CallSites.Where(
                    item => !string.Equals(
                        item.CallSiteId,
                        mutated.CallSiteId,
                        StringComparison.Ordinal)))
            {
                mutated
            };

            var resolution = whitelist.Resolve(
                whitelist.Assembly,
                actual);

            Assert.AreEqual(
                RngWhitelistResolutionStatus.CallSiteMismatch,
                resolution.Status);
            StringAssert.Contains(
                resolution.Detail,
                "helper.random-vector2");
        }
    }
}
