using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Automation.Client;
using HollowKnightTAS.Companion.Automation;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Automation;
using HollowKnightTAS.Core.Ipc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class WorldObservationSdkTests
    {
        [TestMethod]
        [Timeout(30000)]
        public async Task WorldSnapshotJsonAggregatesPagesAndPreservesSamplingFrame()
        {
            await WithClientAsync(Fault.None, async client =>
            {
                var json = await client.GetWorldSnapshotJsonAsync(limit: 2);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                Assert.AreEqual("snapshot-test-id", root.GetProperty("snapshotId").GetString());
                Assert.AreEqual(7, root.GetProperty("nativeFrame").GetInt64());
                Assert.AreEqual(42, root.GetProperty("movieFrame").GetInt64());
                Assert.AreEqual(3, root.GetProperty("total").GetInt32());
                Assert.AreEqual(-1, root.GetProperty("nextOffset").GetInt32());
                Assert.AreEqual(3, root.GetProperty("objects").GetArrayLength());
            });
        }

        [TestMethod]
        [Timeout(30000)]
        public async Task WorldSnapshotJsonRejectsChangedSnapshotIdentityOrSamplingFrame()
        {
            await WithClientAsync(Fault.SnapshotId, async client =>
                await Assert.ThrowsExactlyAsync<InvalidDataException>(
                    () => client.GetWorldSnapshotJsonAsync(limit: 2)));
            await WithClientAsync(Fault.SnapshotFrame, async client =>
                await Assert.ThrowsExactlyAsync<InvalidDataException>(
                    () => client.GetWorldSnapshotJsonAsync(limit: 2)));
        }

        [TestMethod]
        [Timeout(30000)]
        public async Task ObjectDetailsJsonAggregatesFragmentsAndRejectsCursorOrHashChanges()
        {
            await WithClientAsync(Fault.None, async client =>
            {
                var json = await client.GetObjectDetailsJsonAsync(
                    "hero:1", expectedNativeFrame: 7, maxCharacters: 1024);
                Assert.AreEqual("{\"objectId\":\"hero:1\",\"components\":[1,2,3]}", json);
            });
            await WithClientAsync(Fault.DetailsCursor, async client =>
                await Assert.ThrowsExactlyAsync<InvalidDataException>(
                    () => client.GetObjectDetailsJsonAsync(
                        "hero:1", expectedNativeFrame: 7, maxCharacters: 1024)));
            await WithClientAsync(Fault.DetailsHash, async client =>
                await Assert.ThrowsExactlyAsync<InvalidDataException>(
                    () => client.GetObjectDetailsJsonAsync(
                        "hero:1", expectedNativeFrame: 7, maxCharacters: 1024)));
        }

        private static async Task WithClientAsync(
            Fault fault, Func<IAutomationClient, Task> action)
        {
            var fixture = Fixture.Create(fault);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var fakeRuntime = RunFakeRuntimeAsync(fixture, cancellation.Token);
            using var sessions = new SessionRegistry(
                "world-observation-sdk-" + Guid.NewGuid().ToString("N"));

            using var boot = new StartupBootController();
            var coordinator = new FullRunMovieCoordinator(boot);
            var gate = boot.BeginV2();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(FullRunMovieCoordinator).GetField("gate", flags)!.SetValue(coordinator, gate);
            typeof(FullRunMovieCoordinator).GetField("mode", flags)!.SetValue(coordinator, "Replay");

            var automationRoot = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "HollowKnightTAS.Tests",
                "world-observation-sdk-" + Guid.NewGuid().ToString("N"));
            using var broker = new AutomationBroker(
                sessions, automationDirectory: automationRoot,
                fullRunMovies: coordinator);
            broker.BindFullRunEndpoint(
                gate.Token, fixture.Registration.EnvironmentManifestSha256,
                fixture.Registration.AutomationMode);
            Assert.IsTrue(await sessions.RegisterAsync(fixture.Registration, cancellation.Token));

            try
            {
                await using var client = new AutomationClient();
                await client.ConnectAsync(new AutomationConnectOptions
                {
                    ClientId = "world-observation-sdk-client",
                    BootstrapPath = broker.BootstrapPath
                }, cancellation.Token);
                await action(client);
            }
            finally
            {
                cancellation.Cancel();
                try { await fakeRuntime; }
                catch (OperationCanceledException) { }
            }
        }

        private static async Task RunFakeRuntimeAsync(
            Fixture fixture, CancellationToken cancellationToken)
        {
            using var server = new NamedPipeServerStream(
                fixture.Registration.PipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await server.WaitForConnectionAsync(cancellationToken);
            var hello = await IpcCodec.ReadFrameAsync(server, cancellationToken);
            var validation = IpcHandshakeValidator.ValidateHello(
                hello, fixture.Registration.SessionId,
                fixture.Registration.GameProcessId,
                fixture.Registration.ProtocolRange,
                fixture.Registration.Token);
            Assert.IsTrue(validation.Success, validation.Error);
            var nonce = new byte[32];
            RandomNumberGenerator.Fill(nonce);
            await IpcCodec.WriteFrameAsync(server, new IpcEnvelope(
                1, fixture.Registration.SessionId, 0, IpcMessageTypes.HelloAck,
                IpcPayloadCodec.Serialize(new Dictionary<string, string>
                {
                    ["companionInstanceId"] = validation.CompanionInstanceId,
                    ["protocol"] = "1",
                    ["runtimeSessionId"] = fixture.Registration.SessionId,
                    ["serverNonce"] = Convert.ToBase64String(nonce)
                })), cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                var command = await IpcCodec.ReadFrameAsync(server, cancellationToken);
                var parsed = IpcPayloadCodec.TryDeserialize(command.PayloadUtf8);
                Assert.IsTrue(parsed.Success && parsed.Fields != null, "Fake Runtime received invalid payload.");
                var fields = parsed.Fields!;
                if (!fields.TryGetValue("requestId", out var requestId)) continue;
                if (command.MessageType == IpcMessageTypes.GetWorldSnapshot)
                {
                    var offset = int.Parse(fields.GetValueOrDefault("offset", "0"), CultureInfo.InvariantCulture);
                    await SendWorldPageAsync(server, fixture, requestId, offset, cancellationToken);
                }
                else if (command.MessageType == IpcMessageTypes.GetObjectDetails)
                {
                    var cursor = int.Parse(fields.GetValueOrDefault("cursor", "0"), CultureInfo.InvariantCulture);
                    await SendDetailsPageAsync(server, fixture, requestId, cursor, cancellationToken);
                }
            }
        }

        private static async Task SendWorldPageAsync(
            NamedPipeServerStream server, Fixture fixture, string requestId,
            int offset, CancellationToken token)
        {
            var snapshotId = offset == 0 || fixture.Fault != Fault.SnapshotId
                ? "snapshot-test-id" : "snapshot-test-other";
            var nativeFrame = offset == 0 || fixture.Fault != Fault.SnapshotFrame ? 7 : 8;
            var values = new[]
            {
                new { id = "hero:1", kind = "hero" },
                new { id = "enemy:1", kind = "enemy" },
                new { id = "terrain:1", kind = "collider" }
            };
            var pageValues = values.Skip(offset).Take(2).ToArray();
            var next = offset + pageValues.Length < values.Length ? offset + pageValues.Length : -1;
            var snapshotJson = JsonSerializer.Serialize(new
            {
                schemaVersion = 1, snapshotId, nativeFrame, movieFrame = 42,
                metadata = new { activeScene = new { name = "TestScene", path = "Scenes/TestScene" } },
                total = values.Length, offset, objects = pageValues, nextOffset = next
            });
            await SendAsync(server, fixture.Registration.SessionId,
                IpcMessageTypes.WorldSnapshot, new Dictionary<string, string>
                {
                    ["requestId"] = requestId, ["snapshotId"] = snapshotId,
                    ["nativeFrame"] = nativeFrame.ToString(CultureInfo.InvariantCulture),
                    ["movieFrame"] = "42", ["snapshotJson"] = snapshotJson
                }, fixture, token);
        }

        private static async Task SendDetailsPageAsync(
            NamedPipeServerStream server, Fixture fixture, string requestId,
            int cursor, CancellationToken token)
        {
            const string json = "{\"objectId\":\"hero:1\",\"components\":[1,2,3]}";
            const string detailsId = "details-test-id";
            var split = Math.Min(7, json.Length);
            var length = cursor == 0 ? split : json.Length - cursor;
            if (cursor < 0 || cursor > json.Length) length = 0;
            var fragment = json.Substring(Math.Min(cursor, json.Length), length);
            var responseCursor = fixture.Fault == Fault.DetailsCursor && cursor > 0 ? 0 : cursor;
            var next = cursor + length < json.Length ? cursor + length : -1;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
            if (fixture.Fault == Fault.DetailsHash) hash = new string('0', 64);
            await SendAsync(server, fixture.Registration.SessionId,
                IpcMessageTypes.ObjectDetails, new Dictionary<string, string>
                {
                    ["requestId"] = requestId, ["detailsId"] = detailsId,
                    ["objectId"] = "hero:1", ["nativeFrame"] = "7", ["movieFrame"] = "42",
                    ["detailsJson"] = fragment, ["cursor"] = responseCursor.ToString(CultureInfo.InvariantCulture),
                    ["nextCursor"] = next.ToString(CultureInfo.InvariantCulture),
                    ["totalCharacters"] = json.Length.ToString(CultureInfo.InvariantCulture),
                    ["complete"] = next == -1 ? "true" : "false", ["sha256"] = hash
                }, fixture, token);
        }

        private static Task SendAsync(
            NamedPipeServerStream server, string sessionId, string messageType,
            IReadOnlyDictionary<string, string> fields, Fixture fixture,
            CancellationToken token)
            => IpcCodec.WriteFrameAsync(server, new IpcEnvelope(
                1, sessionId, ++fixture.OutgoingSequence, messageType,
                IpcPayloadCodec.Serialize(fields)), token);

        private enum Fault
        {
            None,
            SnapshotId,
            SnapshotFrame,
            DetailsCursor,
            DetailsHash
        }

        private sealed class Fixture
        {
            private Fixture(CompanionSessionRegistration registration, Fault fault)
            {
                Registration = registration;
                Fault = fault;
            }

            public CompanionSessionRegistration Registration { get; }
            public Fault Fault { get; }
            public long OutgoingSequence { get; set; }

            public static Fixture Create(Fault fault)
            {
                var suffix = Guid.NewGuid().ToString("N");
                var token = new byte[32];
                RandomNumberGenerator.Fill(token);
                return new Fixture(new CompanionSessionRegistration(
                    "world-observation-sdk-" + suffix, Environment.ProcessId,
                    Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
                    new string('a', 64), new string('b', 64), new string('c', 64),
                    "HollowKnightTAS.Runtime.WorldObservationSdk." + suffix,
                    CompanionProtocolMetadata.SupportedProtocols, false, token,
                    AutomationMode.ApprovedControl, false), fault);
            }
        }
    }
}
