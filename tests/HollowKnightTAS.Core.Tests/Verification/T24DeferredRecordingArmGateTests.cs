using System;
using System.Threading;
using HollowKnightTAS.Runtime.Control;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Verification
{
    [TestClass]
    public sealed class T24DeferredRecordingArmGateTests
    {
        [TestMethod]
        public void MissingArgumentDisablesGateWithoutError()
        {
            var success = T24DeferredRecordingArmGate.TryParseRunId(
                new[] { "hollow_knight.exe", "--unrelated" },
                out var runId,
                out var error);

            Assert.IsTrue(success);
            Assert.AreEqual(string.Empty, runId);
            Assert.AreEqual(string.Empty, error);
        }

        [TestMethod]
        public void ExactArgumentParsesValidatedRunId()
        {
            var expected = "candidate-tas-sequential-20260824T120000000Z";
            var success = T24DeferredRecordingArmGate.TryParseRunId(
                new[]
                {
                    "hollow_knight.exe",
                    T24DeferredRecordingArmGate.ArgumentPrefix + expected
                },
                out var runId,
                out var error);

            Assert.IsTrue(success);
            Assert.AreEqual(expected, runId);
            Assert.AreEqual(string.Empty, error);
        }

        [TestMethod]
        public void DuplicateOrInvalidArgumentsFailClosed()
        {
            var duplicate = T24DeferredRecordingArmGate.TryParseRunId(
                new[]
                {
                    T24DeferredRecordingArmGate.ArgumentPrefix + "one",
                    T24DeferredRecordingArmGate.ArgumentPrefix + "two"
                },
                out var duplicateRunId,
                out var duplicateError);
            var invalid = T24DeferredRecordingArmGate.TryParseRunId(
                new[]
                {
                    T24DeferredRecordingArmGate.ArgumentPrefix + "bad/run"
                },
                out var invalidRunId,
                out var invalidError);

            Assert.IsFalse(duplicate);
            Assert.AreEqual(string.Empty, duplicateRunId);
            StringAssert.Contains(duplicateError, "Exactly one");
            Assert.IsFalse(invalid);
            Assert.AreEqual(string.Empty, invalidRunId);
            StringAssert.Contains(invalidError, "invalid character");
        }

        [TestMethod]
        public void ManualResetReleaseIsConsumedExactlyOnce()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("Named EventWaitHandle verification is Windows-only.");
            }

            var runId = "unit-" + Guid.NewGuid().ToString("N");
            using var gate = new T24DeferredRecordingArmGate(runId);
            using var host = new EventWaitHandle(
                false,
                EventResetMode.ManualReset,
                T24DeferredRecordingArmGate.EventPrefix + runId);

            Assert.IsFalse(gate.TryConsumeRelease());
            Assert.IsFalse(gate.ReleaseConsumed);
            Assert.IsTrue(host.Set());
            Assert.IsTrue(gate.TryConsumeRelease());
            Assert.IsTrue(gate.ReleaseConsumed);
            Assert.IsFalse(gate.TryConsumeRelease());
        }
    }
}
