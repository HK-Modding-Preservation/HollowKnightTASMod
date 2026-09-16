using System;
using System.Linq;
using HollowKnightTAS.Core.Capabilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Capabilities
{
    [TestClass]
    public sealed class CapabilityDescriptorTests
    {
        [TestMethod]
        public void NativeCatalogDoesNotOverclaimCheckpoint()
        {
            var disabled =
                NativeCapabilityCatalog.Create(false);
            var requested =
                NativeCapabilityCatalog.Create(true);

            Assert.AreEqual(
                CapabilityStatus.ExperimentalDisabled,
                disabled.Single(
                    item =>
                        item.CapabilityId
                        == NativeCapabilityCatalog.ProcessObserve)
                    .Status);
            Assert.AreEqual(
                CapabilityStatus.ExperimentalEnabled,
                requested.Single(
                    item =>
                        item.CapabilityId
                        == NativeCapabilityCatalog.ProcessObserve)
                    .Status);
            Assert.AreEqual(
                CapabilityStatus.Unsupported,
                requested.Single(
                    item =>
                        item.CapabilityId
                        == NativeCapabilityCatalog.Checkpoint)
                    .Status);
            var observe = requested.Single(
                item =>
                    item.CapabilityId
                    == NativeCapabilityCatalog.ProcessObserve);
            Assert.AreEqual(
                CapabilityPermission.ProcessQuery,
                observe.Permissions);
            Assert.IsTrue(observe.RequiresNativeHost);
            Assert.IsFalse(observe.RequiresBridge);
            CollectionAssert.Contains(
                observe.BuildWhitelistIds.ToArray(),
                NativeCapabilityCatalog.SupportedBuildId);
            CollectionAssert.Contains(
                observe.SupportedArchitectures.ToArray(),
                "x64");
            Assert.AreEqual(
                64L * 1024L * 1024L,
                observe.MaximumMemoryBytes);
            Assert.AreEqual("pending", observe.EvidenceSha256);
            Assert.AreEqual(
                CapabilityPermission.None,
                requested.Single(
                    item =>
                        item.CapabilityId
                        == NativeCapabilityCatalog.Checkpoint)
                    .Permissions);
        }

        [TestMethod]
        public void CapabilityIdsAndBudgetsFailClosed()
        {
            Assert.IsFalse(
                CapabilityDescriptor.IsValidId(
                    "native/../../shell"));
            Assert.IsFalse(
                CapabilityDescriptor.IsValidId(
                    "Native.Process"));
            var rejected = false;
            try
            {
                _ = new CapabilityDescriptor(
                    NativeCapabilityCatalog.ProcessObserve,
                    1,
                    CapabilityStatus.Verified,
                    CapabilityPermission.ProcessQuery,
                    true,
                    false,
                    false,
                    new[] { "windows-11" },
                    new[] { "x64" },
                    new[]
                    {
                        NativeCapabilityCatalog
                            .SupportedBuildId
                    },
                    0,
                    1,
                    1,
                    Array.Empty<string>(),
                    "fallback",
                    "pending");
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }

            Assert.IsTrue(rejected);
        }
    }
}
