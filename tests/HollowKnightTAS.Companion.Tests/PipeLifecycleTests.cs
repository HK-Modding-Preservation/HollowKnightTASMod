using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Ipc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class PipeLifecycleTests
    {
        [TestMethod]
        [Timeout(30000)]
        public async Task RuntimeClientReconnectsOneHundredTimes()
        {
            for (var iteration = 0;
                 iteration < 100;
                 iteration++)
            {
                var fixture = CreateFixture(iteration);
                var serverTask = RunFakeRuntimeAsync(
                    fixture,
                    expectPing: true);
                using (var client =
                       new RuntimeSessionClient(
                           fixture.Registration,
                           "companion-reconnect"))
                {
                    var pong =
                        new TaskCompletionSource<bool>(
                            TaskCreationOptions
                                .RunContinuationsAsynchronously);
                    client.EnvelopeReceived +=
                        (_, envelope) =>
                        {
                            if (string.Equals(
                                    envelope.MessageType,
                                    IpcMessageTypes.Pong,
                                    StringComparison.Ordinal))
                            {
                                pong.TrySetResult(true);
                            }
                        };
                    await client.ConnectAsync(
                        TimeSpan.FromSeconds(2),
                        CancellationToken.None);
                    Assert.IsTrue(client.IsConnected);
                    await client.SendCommandAsync(
                        IpcMessageTypes.Ping,
                        new Dictionary<string, string>(
                            StringComparer.Ordinal),
                        CancellationToken.None);
                    Assert.IsTrue(
                        await pong.Task.WaitAsync(
                            TimeSpan.FromSeconds(2)));
                }

                var command = await serverTask;
                Assert.IsNotNull(command);
                Assert.AreEqual(
                    IpcMessageTypes.Ping,
                    command.MessageType);
                Assert.AreEqual(1L, command.Sequence);
            }
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task ControlPipeRegistersAndAuthenticatesShutdown()
        {
            var fixture = CreateFixture(1001);
            var runtimeTask = RunFakeRuntimeAsync(
                fixture,
                expectPing: false);
            using (var sessions =
                   new SessionRegistry(
                       "companion-control-test"))
            using (var control =
                   new ControlPipeServer(
                       "HollowKnightTAS.Companion.Test."
                       + Guid.NewGuid().ToString("N"),
                       sessions))
            {
                control.Start();
                var registerAck = await SendControlAsync(
                    control,
                    fixture.Registration,
                    IpcMessageTypes.RegisterSession,
                    IpcMessageTypes.RegisterSessionAck);
                AssertMetadata(
                    registerAck,
                    expectedAccepted: true);
                Assert.AreEqual(1, sessions.ConnectedCount);

                var shutdownAck = await SendControlAsync(
                    control,
                    fixture.Registration,
                    IpcMessageTypes.ShutdownSession,
                    IpcMessageTypes.ControlAck);
                AssertMetadata(
                    shutdownAck,
                    expectedAccepted: true);
                Assert.AreEqual(0, sessions.ConnectedCount);
            }

            await runtimeTask;
        }

        private static async Task<IpcEnvelope> SendControlAsync(
            ControlPipeServer server,
            CompanionSessionRegistration registration,
            string messageType,
            string expectedResponseType)
        {
            using (var timeout =
                   new CancellationTokenSource(
                       TimeSpan.FromSeconds(3)))
            using (var client = new NamedPipeClientStream(
                       ".",
                       server.PipeName,
                       PipeDirection.InOut,
                       PipeOptions.Asynchronous))
            {
                await client.ConnectAsync(timeout.Token);
                await IpcCodec.WriteFrameAsync(
                    client,
                    new IpcEnvelope(
                        1,
                        registration.SessionId,
                        0,
                        messageType,
                        registration.ToPayload()),
                    timeout.Token);
                var response = await IpcCodec.ReadFrameAsync(
                    client,
                    timeout.Token);
                Assert.AreEqual(
                    expectedResponseType,
                    response.MessageType);
                return response;
            }
        }

        private static void AssertMetadata(
            IpcEnvelope response,
            bool expectedAccepted)
        {
            var payload = IpcPayloadCodec.TryDeserialize(
                response.PayloadUtf8);
            Assert.IsTrue(payload.Success);
            Assert.IsNotNull(payload.Fields);
            Assert.AreEqual(7, payload.Fields.Count);
            Assert.AreEqual(
                expectedAccepted ? "true" : "false",
                payload.Fields["accepted"]);
            Assert.AreEqual(
                CompanionProtocolMetadata.Product,
                payload.Fields["product"]);
            Assert.AreEqual(
                CompanionProtocolMetadata.Version,
                payload.Fields["version"]);
            Assert.AreEqual(
                "1",
                payload.Fields["protocolMin"]);
            Assert.AreEqual(
                "1",
                payload.Fields["protocolMax"]);
            Assert.IsTrue(
                IpcIdentifier.IsValid(
                    payload.Fields["companionInstanceId"],
                    128));
        }

        private static async Task<IpcEnvelope> RunFakeRuntimeAsync(
            PipeFixture fixture,
            bool expectPing)
        {
            using (var timeout =
                   new CancellationTokenSource(
                       TimeSpan.FromSeconds(5)))
            using (var server = new NamedPipeServerStream(
                       fixture.Registration.PipeName,
                       PipeDirection.InOut,
                       1,
                       PipeTransmissionMode.Byte,
                       PipeOptions.Asynchronous
                       | PipeOptions.CurrentUserOnly))
            {
                await server.WaitForConnectionAsync(
                    timeout.Token);
                var hello = await IpcCodec.ReadFrameAsync(
                    server,
                    timeout.Token);
                var validation =
                    IpcHandshakeValidator.ValidateHello(
                        hello,
                        fixture.Registration.SessionId,
                        fixture.Registration.GameProcessId,
                        fixture.Registration.ProtocolRange,
                        fixture.Registration.Token);
                Assert.IsTrue(
                    validation.Success,
                    validation.Error);
                var nonce = new byte[32];
                RandomNumberGenerator.Fill(nonce);
                await IpcCodec.WriteFrameAsync(
                    server,
                    new IpcEnvelope(
                        validation.NegotiatedProtocol,
                        fixture.Registration.SessionId,
                        0,
                        IpcMessageTypes.HelloAck,
                        IpcPayloadCodec.Serialize(
                            new Dictionary<string, string>(
                                StringComparer.Ordinal)
                            {
                                ["companionInstanceId"] =
                                    validation
                                        .CompanionInstanceId,
                                ["protocol"] =
                                    validation
                                        .NegotiatedProtocol
                                        .ToString(
                                            CultureInfo
                                                .InvariantCulture),
                                ["runtimeSessionId"] =
                                    fixture
                                        .Registration
                                        .SessionId,
                                ["serverNonce"] =
                                    Convert.ToBase64String(
                                        nonce)
                            })),
                    timeout.Token);
                if (expectPing)
                {
                    var command =
                        await IpcCodec.ReadFrameAsync(
                            server,
                            timeout.Token);
                    await IpcCodec.WriteFrameAsync(
                        server,
                        new IpcEnvelope(
                            validation.NegotiatedProtocol,
                            fixture.Registration.SessionId,
                            1,
                            IpcMessageTypes.Pong,
                            IpcPayloadCodec.Serialize(
                                new Dictionary<string, string>(
                                    StringComparer.Ordinal))),
                        timeout.Token);
                    var closeBuffer = new byte[1];
                    while (await server.ReadAsync(
                               closeBuffer,
                               timeout.Token) != 0)
                    {
                    }

                    return command;
                }

                var buffer = new byte[1];
                while (await server.ReadAsync(
                           buffer,
                           timeout.Token) != 0)
                {
                }

                return hello;
            }
        }

        private static PipeFixture CreateFixture(
            int iteration)
        {
            var token = new byte[32];
            RandomNumberGenerator.Fill(token);
            var suffix =
                iteration.ToString(
                    CultureInfo.InvariantCulture)
                + "."
                + Guid.NewGuid().ToString("N");
            var registration =
                new CompanionSessionRegistration(
                    "pipe-test-" + suffix,
                    Environment.ProcessId,
                    Process.GetCurrentProcess()
                        .StartTime
                        .ToUniversalTime()
                        .Ticks,
                    new string('a', 64),
                    new string('b', 64),
                    new string('c', 64),
                    "HollowKnightTAS.Runtime.Test."
                    + suffix,
                    CompanionProtocolMetadata
                        .SupportedProtocols,
                    false,
                    token);
            return new PipeFixture(registration);
        }

        private sealed class PipeFixture
        {
            public PipeFixture(
                CompanionSessionRegistration registration)
            {
                Registration = registration;
            }

            public CompanionSessionRegistration Registration
            {
                get;
            }
        }
    }
}
