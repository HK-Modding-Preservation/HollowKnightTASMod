using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HollowKnightTAS.Companion.Automation;
using HollowKnightTAS.Core.Automation;
using HollowKnightTAS.Core.Ipc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class AutomationSafetyTests
    {
        private const string Hash =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        [TestMethod]
        public void VideoExportCommandsRequireApprovedPlaybackLease()
        {
            foreach (var mode in new[] { AutomationMode.ReadOnly, AutomationMode.ApprovedControl })
            {
                var catalog = new AutomationCapabilityCatalog(mode, false);
                foreach (var command in new[] { AutomationCommandIds.StartVideoExport,
                    AutomationCommandIds.FinishVideoExport, AutomationCommandIds.CancelVideoExport })
                {
                    Assert.IsTrue(AutomationCommandIds.IsKnown(command));
                    Assert.IsTrue(IpcMessageTypes.IsRuntimeCommand(command));
                    Assert.IsTrue(catalog.TryGet(command, out var capability));
                    Assert.IsFalse(capability.ReadOnly);
                    Assert.IsTrue(capability.RequiresLease);
                    Assert.AreEqual(AutomationScope.ControlPlayback, capability.Scope);
                    Assert.AreEqual(mode == AutomationMode.ReadOnly ? "disabled" : "available", capability.Availability);
                }
            }
        }

        [TestMethod]
        public void CompetingClientsHaveExactlyOneLeaseOwner()
        {
            var now = DateTimeOffset.Parse(
                "2026-07-29T00:00:00Z");
            var manager = new ControlLeaseManager(() => now);
            var first = manager.Acquire(
                "client-a",
                "connection-a",
                new[] { AutomationScope.ControlPlayback },
                TimeSpan.FromSeconds(30),
                "session-1",
                Hash,
                AutomationMode.ApprovedControl);
            var second = manager.Acquire(
                "client-b",
                "connection-b",
                new[] { AutomationScope.ControlPlayback },
                TimeSpan.FromSeconds(30),
                "session-1",
                Hash,
                AutomationMode.ApprovedControl);

            Assert.IsTrue(first.Success);
            Assert.IsFalse(second.Success);
            Assert.AreEqual("LeaseBusy", second.Code);
            Assert.IsTrue(
                manager.Validate(
                    "client-a",
                    "connection-a",
                    first.Lease!.LeaseId,
                    AutomationScope.ControlPlayback,
                    "session-1",
                    Hash));
        }

        [TestMethod]
        public void LeaseExpiresAndDisconnectReleasesIt()
        {
            var now = DateTimeOffset.Parse(
                "2026-07-29T00:00:00Z");
            var manager = new ControlLeaseManager(() => now);
            var lease = manager.Acquire(
                "client-a",
                "connection-a",
                new[] { AutomationScope.ControlStep },
                TimeSpan.FromSeconds(30),
                "session-1",
                Hash,
                AutomationMode.ApprovedControl);
            now = now.AddSeconds(31);
            Assert.IsFalse(
                manager.Validate(
                    "client-a",
                    "connection-a",
                    lease.Lease!.LeaseId,
                    AutomationScope.ControlStep,
                    "session-1",
                    Hash));

            var replacement = manager.Acquire(
                "client-b",
                "connection-b",
                new[] { AutomationScope.ControlStep },
                TimeSpan.FromSeconds(30),
                "session-1",
                Hash,
                AutomationMode.ApprovedControl);
            Assert.IsTrue(replacement.Success);
            manager.ReleaseConnection("connection-b");
            Assert.IsNull(manager.Active);
        }

        [TestMethod]
        public void ReadOnlyModeCannotAcquireLease()
        {
            var manager = new ControlLeaseManager();
            var result = manager.Acquire(
                "client-a",
                "connection-a",
                new[] { AutomationScope.ControlPlayback },
                TimeSpan.FromSeconds(30),
                "session-1",
                Hash,
                AutomationMode.ReadOnly);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("ControlNotApproved", result.Code);
        }

        [TestMethod]
        public void AuthenticatorRejectsWrongTokenAndReplayedNonce()
        {
            var token = Enumerable.Repeat((byte)7, 32).ToArray();
            var nonce = Enumerable.Repeat((byte)9, 32).ToArray();
            var authenticator =
                new AutomationSessionAuthenticator(token);
            var valid = Hello(token, nonce);
            Assert.IsTrue(
                authenticator.Authenticate(
                    valid,
                    "session-1",
                    Hash).Success);
            Assert.AreEqual(
                "ReplayedNonce",
                authenticator.Authenticate(
                    valid,
                    "session-1",
                    Hash).ErrorCode);
            var wrong = Hello(
                Enumerable.Repeat((byte)8, 32).ToArray(),
                Enumerable.Repeat((byte)10, 32).ToArray());
            Assert.AreEqual(
                "AuthenticationFailed",
                authenticator.Authenticate(
                    wrong,
                    "session-1",
                    Hash).ErrorCode);
        }

        [TestMethod]
        public void CapabilityCatalogHidesWritesInReadOnly()
        {
            var catalog = new AutomationCapabilityCatalog(
                AutomationMode.ReadOnly,
                false);
            Assert.IsTrue(catalog.TryGet(AutomationCommandIds.GetStartupProfile, out var startup));
            Assert.IsTrue(startup.ReadOnly);
            Assert.IsFalse(startup.RequiresLease);
            Assert.AreEqual("available", startup.Availability);
            Assert.AreEqual(AutomationScope.ObserveStatus, startup.Scope);
            Assert.IsTrue(
                catalog.TryGet(
                    AutomationCommandIds.Pause,
                    out var pause));
            Assert.AreEqual("disabled", pause.Availability);
            Assert.IsTrue(
                catalog.Items
                    .Where(item => item.RequiresLease)
                    .All(item => item.Availability == "disabled"));
            Assert.IsTrue(
                catalog.TryGet(
                    AutomationCommandIds.ProposeMoviePatch,
                    out var proposal));
            Assert.IsFalse(proposal.ReadOnly);
            Assert.IsFalse(proposal.RequiresLease);
            Assert.AreEqual(
                "available",
                proposal.Availability);
            foreach (var commandId in new[]
                     {
                         AutomationCommandIds
                             .ApproveReplaySaveOverwrite,
                         AutomationCommandIds.SetAutoSavePolicy,
                         AutomationCommandIds
                             .CancelReplaySaveRestore,
                         AutomationCommandIds
                             .ResumeReplaySaveRestore
                     })
            {
                Assert.IsTrue(catalog.TryGet(commandId, out var item));
                Assert.AreEqual(
                    AutomationScope.ControlReplaySave,
                    item.Scope);
                Assert.AreEqual("disabled", item.Availability);
                Assert.IsTrue(item.RequiresLease);
            }


            foreach (var commandId in new[]
                     {
                         AutomationCommandIds.GetCombatState,
                         AutomationCommandIds.StepWithInput,
                         AutomationCommandIds.QueueInputBatch,
                         AutomationCommandIds.ReplaceInputRange,
                         AutomationCommandIds.InsertInputRange,
                         AutomationCommandIds.DeleteInputRange
                     })
            {
                Assert.IsTrue(
                    catalog.TryGet(commandId, out _),
                    commandId);
            }
        }

        private static IpcEnvelope Hello(
            byte[] token,
            byte[] nonce)
        {
            return new IpcEnvelope(
                1,
                "session-1",
                0,
                AutomationProtocol.Hello,
                AutomationHandshakeCodec.CreateHello(
                    "client-1",
                    "session-1",
                    Hash,
                    nonce,
                    token));
        }
    }
}
