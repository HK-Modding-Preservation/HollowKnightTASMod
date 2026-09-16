using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HollowKnightTAS.Core.Automation;
using HollowKnightTAS.Core.Ipc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Automation
{
    [TestClass]
    public sealed class AutomationProtocolTests
    {
        private const string Hash =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        [TestMethod]
        public void CommandCanonicalBytesAreStableAcrossOneHundredRuns()
        {
            var command = CreateCommand();
            var expected = command.ToPayload();
            for (var iteration = 0; iteration < 100; iteration++)
            {
                CollectionAssert.AreEqual(
                    expected,
                    CreateCommand().ToPayload());
            }

            Assert.IsTrue(
                AutomationCommandEnvelope.TryParse(
                    expected,
                    out var parsed,
                    out _,
                    out _));
            CollectionAssert.AreEqual(
                expected,
                parsed!.ToPayload());
        }

        [TestMethod]
        public void CommandMatchesPublishedCanonicalFixture()
        {
            var fixture = File.ReadAllText(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "fixtures",
                        "automation",
                        "canonical-command-v1.json"),
                    new UTF8Encoding(false, true))
                .TrimEnd('\r', '\n');
            Assert.AreEqual(
                fixture,
                Encoding.UTF8.GetString(
                    CreateCommand().ToPayload()));
        }

        [TestMethod]
        public void CommandRejectsUnknownCommandAndScope()
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new AutomationCommandEnvelope(
                    "request-1",
                    "key-1",
                    "client-1",
                    "session-1",
                    Hash,
                    "shell",
                    AutomationScope.ObserveStatus,
                    string.Empty,
                    string.Empty,
                    null,
                    IpcPayloadCodec.Serialize(
                        new Dictionary<string, string>())));
            Assert.ThrowsExactly<ArgumentException>(
                () => new AutomationCommandEnvelope(
                    "request-1",
                    "key-1",
                    "client-1",
                    "session-1",
                    Hash,
                    AutomationCommandIds.GetStatus,
                    "filesystem.read",
                    string.Empty,
                    string.Empty,
                    null,
                    IpcPayloadCodec.Serialize(
                        new Dictionary<string, string>())));
        }

        [TestMethod]
        public void ReplaySaveLifecycleCommandsAreRegistered()
        {
            foreach (var commandId in new[]
                     {
                         AutomationCommandIds
                             .ApproveReplaySaveOverwrite,
                         AutomationCommandIds
                             .CancelReplaySaveRestore,
                         AutomationCommandIds
                             .ResumeReplaySaveRestore
                     })
            {
                Assert.IsTrue(
                    AutomationCommandIds.IsKnown(commandId),
                    commandId);
            }
        }

        [TestMethod]
        public void CommandRejectsNonCanonicalOrNestedArguments()
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new AutomationCommandEnvelope(
                    "request-1",
                    "key-1",
                    "client-1",
                    "session-1",
                    Hash,
                    AutomationCommandIds.GetStatus,
                    AutomationScope.ObserveStatus,
                    string.Empty,
                    string.Empty,
                    null,
                    Encoding.UTF8.GetBytes("{\"z\":\"1\", \"a\":\"2\"}")));
            Assert.ThrowsExactly<ArgumentException>(
                () => new AutomationCommandEnvelope(
                    "request-1",
                    "key-1",
                    "client-1",
                    "session-1",
                    Hash,
                    AutomationCommandIds.GetStatus,
                    AutomationScope.ObserveStatus,
                    string.Empty,
                    string.Empty,
                    null,
                    Encoding.UTF8.GetBytes("{\"value\":{\"nested\":\"no\"}}")));
        }

        [TestMethod]
        public void BootstrapRoundTripBindsModeAndCredential()
        {
            var token = new byte[32];
            for (var index = 0; index < token.Length; index++)
            {
                token[index] = (byte)index;
            }

            var value = new AutomationBootstrapDescriptor(
                "HollowKnightTAS.Automation.test",
                "session-1",
                Hash,
                AutomationMode.ReadOnly,
                token);
            Assert.IsTrue(
                AutomationBootstrapDescriptor.TryParse(
                    value.ToBytes(),
                    out var parsed,
                    out var error),
                error);
            Assert.AreEqual(AutomationMode.ReadOnly, parsed!.Mode);
            CollectionAssert.AreEqual(token, parsed.Token);
        }

        [TestMethod]
        public void ModeInvalidValueFailsClosedToReadOnly()
        {
            Assert.IsFalse(
                AutomationModeCodec.TryParse(
                    "ApprovedControl ",
                    out var parsed));
            Assert.AreEqual(AutomationMode.ReadOnly, parsed);
            Assert.AreEqual(
                AutomationMode.ReadOnly,
                AutomationModeCodec.Normalize("anything"));
        }

        [TestMethod]
        public void StateCanonicalBytesAreStableAndClosed()
        {
            var mutableFields = new Dictionary<string, string>
            {
                ["verificationEligibility"] = "Eligible",
                ["controlMode"] = "Paused",
                ["playbackMode"] = "Idle",
                ["semanticSnapshotJson"] = "{\"state\":\"fixture\"}"
            };
            var state = new AutomationStateEnvelope(
                "session-1",
                Hash,
                "Paused",
                42,
                "LateUpdateEnd",
                DateTimeOffset.Parse(
                    "2026-07-29T06:00:00.0000000+00:00"),
                7,
                new string('d', 64),
                mutableFields,
                new[] { "getTimeline", "getState" });
            var expected = AutomationStateCodec.Serialize(state);
            for (var iteration = 0; iteration < 100; iteration++)
            {
                Assert.AreEqual(
                    expected,
                    AutomationStateCodec.Serialize(state));
            }

            mutableFields["controlMode"] = "Running";
            Assert.AreEqual(
                expected,
                AutomationStateCodec.Serialize(state));
            using var parsed = JsonDocument.Parse(expected);
            var root = parsed.RootElement;
            Assert.AreEqual(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.AreEqual(
                "Paused",
                root.GetProperty("runtimeMode").GetString());
            CollectionAssert.AreEqual(
                new[] { "getState", "getTimeline" },
                root.GetProperty("activeCapabilities")
                    .EnumerateArray()
                    .Select(item => item.GetString())
                    .ToArray());
            CollectionAssert.AreEqual(
                new[]
                {
                    "controlMode",
                    "playbackMode",
                    "semanticSnapshotJson",
                    "verificationEligibility"
                },
                root.GetProperty("fields")
                    .EnumerateObject()
                    .Select(item => item.Name)
                    .ToArray());
        }

        [TestMethod]
        public void CapabilityCatalogCanonicalBytesAreStable()
        {
            var capabilities = new[]
            {
                new AutomationCapability(
                    "getTimeline",
                    AutomationScope.ObserveTimeline,
                    true,
                    false,
                    "available",
                    "fresh authenticated session",
                    "none"),
                new AutomationCapability(
                    "pause",
                    AutomationScope.ControlPlayback,
                    false,
                    true,
                    "disabled",
                    "ApprovedControl",
                    "pause at safe boundary")
            };
            var expected =
                AutomationCapabilityCodec.SerializeArray(
                    capabilities);
            for (var iteration = 0; iteration < 100; iteration++)
            {
                Assert.AreEqual(
                    expected,
                    AutomationCapabilityCodec.SerializeArray(
                        capabilities.Reverse()));
            }

            using var parsed = JsonDocument.Parse(expected);
            Assert.AreEqual(
                "getTimeline",
                parsed.RootElement[0]
                    .GetProperty("commandId")
                    .GetString());
            Assert.AreEqual(
                "pause",
                parsed.RootElement[1]
                    .GetProperty("commandId")
                    .GetString());
        }

        private static AutomationCommandEnvelope CreateCommand()
        {
            return new AutomationCommandEnvelope(
                "request-1",
                "key-1",
                "client-1",
                "session-1",
                Hash,
                AutomationCommandIds.GetTimeline,
                AutomationScope.ObserveTimeline,
                string.Empty,
                string.Empty,
                42,
                IpcPayloadCodec.Serialize(
                    new Dictionary<string, string>
                    {
                        ["fromMovieTick"] = "42",
                        ["count"] = "100"
                    }));
        }
    }
}
