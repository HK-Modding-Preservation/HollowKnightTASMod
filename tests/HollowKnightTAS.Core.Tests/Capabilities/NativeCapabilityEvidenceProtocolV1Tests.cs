using System;
using System.Collections.Generic;
using System.IO;
using HollowKnightTAS.Core.Capabilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Capabilities
{
    [TestClass]
    public sealed class NativeCapabilityEvidenceProtocolV1Tests
    {
        [TestMethod]
        public void AcceptsCanonicalVerifiedObserveEvidence()
        {
            NativeCapabilityEvidenceProtocolV1.Validate(Valid());
        }

        [TestMethod]
        public void RejectsRawPagePersistenceAndUnexpectedFields()
        {
            var unsafeFields = Valid();
            unsafeFields["rawPagesPersisted"] = "true";
            AssertInvalid(
                () => NativeCapabilityEvidenceProtocolV1.Validate(
                    unsafeFields));

            var expanded = Valid();
            expanded["pageBytes"] = "forbidden";
            AssertInvalid(
                () => NativeCapabilityEvidenceProtocolV1.Validate(
                    expanded));
        }

        [TestMethod]
        public void RejectsNonCanonicalHashesAndCheckpointClaim()
        {
            var badHash = Valid();
            badHash["targetFingerprint"] =
                badHash["targetFingerprint"].ToUpperInvariant();
            AssertInvalid(
                () => NativeCapabilityEvidenceProtocolV1.Validate(
                    badHash));

            var checkpoint = Valid();
            checkpoint["checkpointStatus"] = "verified";
            AssertInvalid(
                () => NativeCapabilityEvidenceProtocolV1.Validate(
                    checkpoint));
        }

        [TestMethod]
        public void AcceptsCanonicalFaultAndRejectsLeakedDetail()
        {
            var fault =
                new SortedDictionary<string, string>(
                    StringComparer.Ordinal)
                {
                    ["errorCode"] = "InvalidDataException",
                    ["fallback"] = "runtime-t09",
                    ["requestId"] = "native-fault-1",
                    ["status"] = "faulted"
                };
            NativeCapabilityEvidenceProtocolV1.Validate(fault);

            fault["detail"] = @"C:\private\path";
            AssertInvalid(
                () => NativeCapabilityEvidenceProtocolV1.Validate(
                    fault));
        }

        [TestMethod]
        public void AcceptsCanonicalStartEvidence()
        {
            NativeCapabilityEvidenceProtocolV1.Validate(
                new SortedDictionary<string, string>(
                    StringComparer.Ordinal)
                {
                    ["capabilityId"] =
                        NativeCapabilityCatalog.ProcessObserve,
                    ["fallback"] = "runtime-t09",
                    ["requestId"] = "native-start-1",
                    ["status"] = "started"
                });
        }

        private static void AssertInvalid(Action action)
        {
            try
            {
                action();
                Assert.Fail("Expected InvalidDataException.");
            }
            catch (InvalidDataException)
            {
            }
        }

        private static SortedDictionary<string, string> Valid()
        {
            return new SortedDictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["assemblyCSharpSha256"] =
                    new string('d', 64),
                ["attachCyclesCompleted"] = "100",
                ["buildWhitelistId"] =
                    "hk-1.5.78.11833-win64-v1",
                ["capabilityId"] =
                    NativeCapabilityCatalog.ProcessObserve,
                ["capabilityVersion"] = "1",
                ["checkpointStatus"] = "unsupported",
                ["coreAssemblySha256"] =
                    new string('f', 64),
                ["environmentManifestSha256"] =
                    new string('1', 64),
                ["evidenceVersion"] = "1",
                ["fallback"] = "none",
                ["imageSha256"] = new string('e', 64),
                ["moduleMapSha256"] = new string('b', 64),
                ["parentProcessVerified"] = "true",
                ["pssCaptureApiAvailable"] = "true",
                ["rawPagesPersisted"] = "false",
                ["requestId"] = "native-evidence-1",
                ["runtimeAssemblySha256"] =
                    new string('2', 64),
                ["status"] = "verified",
                ["targetFingerprint"] = new string('a', 64),
                ["threadSetSha256"] = new string('c', 64)
            };
        }
    }
}
