using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Core.Automation;
using HollowKnightTAS.Core.Ipc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Ipc
{
    [TestClass]
    public sealed class IpcProtocolTests
    {
        [TestMethod]
        public void ProtocolRangeNegotiatesHighestIntersection()
        {
            var runtime = new ProtocolRange(1, 3);
            var companion = new ProtocolRange(2, 4);

            Assert.IsTrue(runtime.TryNegotiate(companion, out var value));
            Assert.AreEqual(3, value);
            Assert.IsFalse(
                runtime.Intersects(new ProtocolRange(4, 5)));
        }

        [TestMethod]
        public void PayloadRoundTripIsCanonicalAndOrdinal()
        {
            var payload = IpcPayloadCodec.Serialize(
                new Dictionary<string, string>
                {
                    ["z"] = "中文",
                    ["a"] = "value\nwith control"
                });

            Assert.AreEqual(
                "{\"a\":\"value\\nwith control\",\"z\":\"中文\"}",
                Encoding.UTF8.GetString(payload));
            var decoded = IpcPayloadCodec.TryDeserialize(payload);
            Assert.IsTrue(decoded.Success, decoded.Error);
            Assert.AreEqual("中文", decoded.Fields!["z"]);
        }

        [TestMethod]
        [DataRow("{ \"a\":\"b\"}", "NonCanonical")]
        [DataRow("{\"a\":1}", "InvalidFieldType")]
        [DataRow("{\"a\":\"b\",\"a\":\"c\"}", "DuplicateField")]
        [DataRow("[]", "MalformedJson")]
        public void PayloadRejectsInvalidJson(
            string json,
            string expectedCode)
        {
            var result = IpcPayloadCodec.TryDeserialize(
                Encoding.UTF8.GetBytes(json));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(expectedCode, result.ErrorCode);
        }

        [TestMethod]
        public void EnvelopeAndFrameRoundTrip()
        {
            var envelope = CreateEnvelope(1, IpcMessageTypes.Ping);
            var json = IpcCodec.SerializeEnvelope(envelope);
            var decoded = IpcCodec.TryDeserializeEnvelope(json);

            Assert.IsTrue(decoded.Success, decoded.Error);
            Assert.AreEqual("session-1", decoded.Envelope!.SessionId);
            Assert.AreEqual(1L, decoded.Envelope.Sequence);
            CollectionAssert.AreEqual(
                envelope.PayloadUtf8,
                decoded.Envelope.PayloadUtf8);

            var frame = IpcCodec.EncodeFrame(envelope);
            Assert.AreEqual(0, frame[0]);
            Assert.AreEqual(0, frame[1]);
            var declared = (frame[2] << 8) | frame[3];
            Assert.AreEqual(frame.Length - 4, declared);
        }

        [TestMethod]
        public async Task StreamRoundTripHandlesFragmentedReads()
        {
            var envelope = CreateEnvelope(1, IpcMessageTypes.Ping);
            var frame = IpcCodec.EncodeFrame(envelope);
            using (var stream = new FragmentedReadStream(frame, 3))
            {
                var decoded = await IpcCodec.ReadFrameAsync(
                    stream,
                    CancellationToken.None);
                Assert.AreEqual(IpcMessageTypes.Ping, decoded.MessageType);
            }
        }

        [TestMethod]
        [DataRow("{\"protocolVersion\":1,\"sessionId\":\"session-1\",\"sequence\":1,\"messageType\":\"ping\",\"payload\":{}} ", "NonCanonical")]
        [DataRow("{\"sessionId\":\"session-1\",\"protocolVersion\":1,\"sequence\":1,\"messageType\":\"ping\",\"payload\":{}}", "InvalidShape")]
        [DataRow("{\"protocolVersion\":1,\"sessionId\":\"session-1\",\"sequence\":1,\"messageType\":\"ping\",\"payload\":{},\"extra\":\"x\"}", "InvalidShape")]
        [DataRow("{\"protocolVersion\":1,\"sessionId\":\"session-1\",\"sequence\":1,\"messageType\":\"ping\",\"payload\":[]}", "InvalidShape")]
        public void EnvelopeRejectsInvalidShape(
            string json,
            string expectedCode)
        {
            var result = IpcCodec.TryDeserializeEnvelope(
                Encoding.UTF8.GetBytes(json));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(expectedCode, result.ErrorCode);
        }

        [TestMethod]
        public async Task FrameRejectsOversizeBeforeAllocatingBody()
        {
            using (var stream = new MemoryStream(
                       new byte[] { 0, 16, 0, 1 }))
            {
                IpcProtocolException? exception = null;
                try
                {
                    await IpcCodec.ReadFrameAsync(
                        stream,
                        CancellationToken.None);
                }
                catch (IpcProtocolException caught)
                {
                    exception = caught;
                }

                Assert.IsNotNull(exception);
                Assert.AreEqual(
                    "InvalidFrameLength",
                    exception!.Code);
            }
        }

        [TestMethod]
        public async Task FrameRejectsTruncationAndMalformedUtf8()
        {
            using (var truncated =
                   new MemoryStream(
                       new byte[] { 0, 0, 0, 10, 1, 2, 3 }))
            {
                var rejected = false;
                try
                {
                    await IpcCodec.ReadFrameAsync(
                        truncated,
                        CancellationToken.None);
                }
                catch (EndOfStreamException)
                {
                    rejected = true;
                }

                Assert.IsTrue(rejected);
            }

            using (var malformed =
                   new MemoryStream(
                       new byte[]
                       {
                           0, 0, 0, 2,
                           0xc3, 0x28
                       }))
            {
                var exception = await CaptureProtocolExceptionAsync(
                    malformed);
                Assert.AreEqual(
                    "MalformedUtf8",
                    exception.Code);
            }
        }

        [TestMethod]
        public void HelloBindsTokenNoncePidSessionAndProtocol()
        {
            var token = Enumerable.Range(0, 32)
                .Select(value => (byte)value)
                .ToArray();
            var nonce = Enumerable.Range(32, 32)
                .Select(value => (byte)value)
                .ToArray();
            var payload = IpcHandshakeValidator.CreateHelloPayload(
                "0.1.0",
                new ProtocolRange(1, 1),
                1234,
                "session-1",
                "companion-1",
                nonce,
                token);
            var envelope = new IpcEnvelope(
                1,
                "session-1",
                0,
                IpcMessageTypes.Hello,
                payload);

            var result = IpcHandshakeValidator.ValidateHello(
                envelope,
                "session-1",
                1234,
                new ProtocolRange(1, 1),
                token);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(1, result.NegotiatedProtocol);
            Assert.AreEqual("companion-1", result.CompanionInstanceId);
            CollectionAssert.AreEqual(nonce, result.ClientNonce);
        }

        [TestMethod]
        public void HelloRejectsWrongTokenWithoutLeakingIt()
        {
            var expected = new byte[32];
            var supplied = new byte[32];
            supplied[31] = 1;
            var payload = IpcHandshakeValidator.CreateHelloPayload(
                "0.1.0",
                new ProtocolRange(1, 1),
                1234,
                "session-1",
                "companion-1",
                new byte[32],
                supplied);
            var result = IpcHandshakeValidator.ValidateHello(
                new IpcEnvelope(
                    1,
                    "session-1",
                    0,
                    IpcMessageTypes.Hello,
                    payload),
                "session-1",
                1234,
                new ProtocolRange(1, 1),
                expected);

            Assert.IsFalse(result.Success);
            Assert.AreEqual("AuthenticationFailed", result.ErrorCode);
            Assert.IsFalse(
                result.Error.Contains(
                    Convert.ToBase64String(supplied),
                    StringComparison.Ordinal));
        }

        [TestMethod]
        public void SequenceRejectsDuplicateGapAndOutOfOrder()
        {
            var validator = new IpcSequenceValidator(0);
            Assert.IsTrue(validator.TryAccept(1, out _));
            Assert.IsFalse(validator.TryAccept(1, out _));
            Assert.IsFalse(validator.TryAccept(3, out _));
            Assert.IsTrue(validator.TryAccept(2, out _));
        }

        [TestMethod]
        public void BoundedQueueRejectsWithoutDroppingAcceptedItems()
        {
            var queue = new BoundedIpcQueue<int>(2);
            Assert.IsTrue(queue.TryEnqueue(1));
            Assert.IsTrue(queue.TryEnqueue(2));
            Assert.IsFalse(queue.TryEnqueue(3));
            Assert.AreEqual(1L, queue.RejectedCount);
            Assert.IsTrue(queue.TryDequeue(out var first));
            Assert.IsTrue(queue.TryDequeue(out var second));
            Assert.AreEqual(1, first);
            Assert.AreEqual(2, second);
        }

        [TestMethod]
        public void RuntimeCommandWhitelistRejectsProcessLikeNames()
        {
            Assert.IsTrue(
                IpcMessageTypes.IsRuntimeCommand(
                    IpcMessageTypes.StartReplay));
            Assert.IsTrue(
                IpcMessageTypes.IsRuntimeCommand(
                    IpcMessageTypes.ReportNativeEvidence));
            Assert.IsTrue(
                IpcMessageTypes.IsRuntimeEvent(
                    IpcMessageTypes.NativeCapabilityEvidence));
            Assert.IsFalse(
                IpcMessageTypes.IsRuntimeCommand("startProcess"));
            Assert.IsFalse(
                IpcMessageTypes.IsRuntimeCommand("runShell"));
        }

        [TestMethod]
        public void DeepObservationCommandsAndResponsesAreWhitelisted()
        {
            Assert.IsTrue(AutomationCommandIds.IsKnown(
                AutomationCommandIds.GetWorldSnapshot));
            Assert.IsTrue(AutomationCommandIds.IsKnown(
                AutomationCommandIds.GetObjectDetails));
            Assert.IsTrue(IpcMessageTypes.IsRuntimeCommand(
                IpcMessageTypes.GetWorldSnapshot));
            Assert.IsTrue(IpcMessageTypes.IsRuntimeCommand(
                IpcMessageTypes.GetObjectDetails));
            Assert.IsTrue(IpcMessageTypes.IsRuntimeEvent(
                IpcMessageTypes.WorldSnapshot));
            Assert.IsTrue(IpcMessageTypes.IsRuntimeEvent(
                IpcMessageTypes.ObjectDetails));
        }

        [TestMethod]
        public void SessionRegistrationRoundTripsWithoutLoggingToken()
        {
            var token = Enumerable.Range(0, 32)
                .Select(value => (byte)value)
                .ToArray();
            var value = new CompanionSessionRegistration(
                "session-1",
                1234,
                638893440000000000L,
                new string('a', 64),
                new string('b', 64),
                new string('c', 64),
                "HollowKnightTAS.Runtime.session-1.abcdef",
                new ProtocolRange(1, 1),
                true,
                token);

            Assert.IsTrue(
                CompanionSessionRegistration.TryParse(
                    value.ToPayload(),
                    out var parsed,
                    out var error),
                error);
            Assert.AreEqual(1234, parsed!.GameProcessId);
            Assert.AreEqual(
                638893440000000000L,
                parsed.GameProcessStartTimeUtcTicks);
            Assert.AreEqual(
                new string('a', 64),
                parsed.EnvironmentManifestSha256);
            Assert.IsTrue(parsed.NativeCapabilitiesRequested);
            CollectionAssert.AreEqual(token, parsed.Token);
            Assert.AreEqual(
                CompanionInstanceNames.CreateControlPipeName("user-a"),
                CompanionInstanceNames.CreateControlPipeName("user-a"));
            Assert.AreNotEqual(
                CompanionInstanceNames.CreateControlPipeName("user-a"),
                CompanionInstanceNames.CreateControlPipeName("user-b"));
        }

        [TestMethod]
        public void StartupAttestationRoundTripsAndRejectsShapeDrift()
        {
            var value = new StartupProfileAttestation(
                StartupProfileAttestationStatus.Verified,
                StartupRecordingRootStatus.Verified,
                "cold-restore-1",
                1234,
                638893440000000000L,
                StartupProfileContract.ProfileId,
                StartupProfileContract.BridgeAbi,
                StartupProfileContract.ReadyBridgeStatus,
                true,
                true,
                true,
                true,
                true,
                0,
                0,
                0,
                0,
                0,
                string.Empty,
                true,
                2,
                true,
                true,
                true,
                0,
                0,
                0);
            var fields = value.ToFields("attestation-1");

            Assert.IsTrue(
                StartupProfileAttestation.TryParse(
                    fields,
                    out var parsed,
                    out var error),
                error);
            Assert.AreEqual(
                StartupProfileAttestationStatus.Verified,
                parsed!.Status);
            Assert.AreEqual(
                StartupRecordingRootStatus.Verified,
                parsed.RootStatus);
            Assert.AreEqual("cold-restore-1", parsed.RunId);

            var drifted = new Dictionary<string, string>(fields)
            {
                ["extra"] = "not-allowed"
            };
            Assert.IsFalse(
                StartupProfileAttestation.TryParse(
                    drifted,
                    out _,
                    out _));
        }

        private static IpcEnvelope CreateEnvelope(
            long sequence,
            string messageType)
        {
            return new IpcEnvelope(
                1,
                "session-1",
                sequence,
                messageType,
                IpcPayloadCodec.Serialize(
                    new Dictionary<string, string>
                    {
                        ["requestId"] = "request-1"
                }));
        }

        private static async Task<IpcProtocolException>
            CaptureProtocolExceptionAsync(Stream stream)
        {
            try
            {
                await IpcCodec.ReadFrameAsync(
                    stream,
                    CancellationToken.None);
            }
            catch (IpcProtocolException exception)
            {
                return exception;
            }

            Assert.Fail("Expected an IPC protocol exception.");
            throw new InvalidOperationException();
        }

        private sealed class FragmentedReadStream : MemoryStream
        {
            private readonly int maximumRead;

            public FragmentedReadStream(
                byte[] buffer,
                int maximumRead)
                : base(buffer)
            {
                this.maximumRead = maximumRead;
            }

            public override Task<int> ReadAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken)
            {
                return base.ReadAsync(
                    buffer,
                    offset,
                    Math.Min(count, maximumRead),
                    cancellationToken);
            }
        }
    }
}
