using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Automation.Client;
using HollowKnightTAS.Companion.Automation;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Automation;
using HollowKnightTAS.Core.Ipc;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class VideoExportArgumentTests
    {
        private static readonly string Hash = new('a', 64);

        [TestMethod]
        [Timeout(15000)]
        public async Task ExistingSdkCallOmitsEndFrameAndNewCallPreservesExactBoundary()
        {
            var original = await CaptureCommandAsync((client, token) => client.StartVideoExportAsync(
                "ffmpeg.exe", "movie.mp4", 10000, "lease-test", "Paused", 1400, true, token));
            Assert.IsFalse(original.Arguments.ContainsKey("endMovieFrame"));
            var bounded = await CaptureCommandAsync((client, token) => client.StartVideoExportAsync(
                "ffmpeg.exe", "movie.mp4", 10000, "lease-test", "Paused", 1400, true, token, endMovieFrame: 3700));
            Assert.AreEqual("3700", bounded.Arguments["endMovieFrame"]);
            Assert.AreEqual(AutomationCommandIds.StartVideoExport, bounded.CommandId);
            Assert.AreEqual(1400L, bounded.ExpectedMovieTick);
            foreach (var field in original.Arguments)
                Assert.AreEqual(field.Value, bounded.Arguments[field.Key]);
            Assert.IsNull(Validate("ValidateArgumentShape", original));
            Assert.IsNull(Validate("ValidateArgumentValues", original));
            Assert.IsNull(Validate("ValidateArgumentShape", bounded));
            Assert.IsNull(Validate("ValidateArgumentValues", bounded));
        }

        [TestMethod]
        public async Task SdkRejectsNonpositiveBoundariesBeforeSending()
        {
            await using var client = new AutomationClient();
            foreach (var invalid in new long[] { 0, -1 })
                Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => client.StartVideoExportAsync(
                    "ffmpeg.exe", "movie.mp4", 10000, "lease-test", "Paused", 1400, endMovieFrame: invalid));
        }

        [TestMethod]
        public void BrokerRejectsInvalidBoundariesAndRetainsStrictArgumentShape()
        {
            foreach (var invalid in new[] { "0", "-1", "10000001", "invalid", "1.5", "1e3", "" })
            {
                var command = Command(invalid);
                Assert.IsNull(Validate("ValidateArgumentShape", command), invalid);
                StringAssert.Contains(Validate("ValidateArgumentValues", command)!, "endMovieFrame");
            }
            Assert.IsNull(Validate("ValidateArgumentValues", Command(MovieProtocolV2.MaximumExpandedFrames.ToString())));
            var fields = Arguments("3700"); fields["startMovieFrame"] = "1400";
            Assert.IsNotNull(Validate("ValidateArgumentShape", Command(fields)));
        }

        [TestMethod]
        public async Task LegacyBrokerExplicitlyRejectsV2BoundaryBeforeRuntimeForwarding()
        {
            var root = Path.Combine(Path.GetTempPath(), "HollowKnightTAS.Tests", "video-arguments-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var sessions = new SessionRegistry("video-arguments-test");
                using var broker = new AutomationBroker(sessions, automationDirectory: root);
                var registration = new CompanionSessionRegistration("video-arguments-session", Environment.ProcessId,
                    Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks, Hash, Hash, Hash,
                    "HollowKnightTAS.Runtime.VideoArguments." + Guid.NewGuid().ToString("N"),
                    CompanionProtocolMetadata.SupportedProtocols, false, new byte[32], AutomationMode.ApprovedControl, false);
                using var session = new RuntimeSessionClient(registration, "video-arguments-test");
                var route = typeof(AutomationBroker).GetMethod("RouteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var task = (Task<AutomationResultEnvelope>)route.Invoke(broker,
                    new object[] { session, "video-arguments-client", "connection-test", Command("3700"), CancellationToken.None })!;
                var result = await task;
                Assert.IsFalse(result.Success);
                Assert.AreEqual("Unsupported", result.ResultCode);
                StringAssert.Contains(result.Detail, "v2");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static Dictionary<string, string> Arguments(string? endFrame)
        {
            var fields = new Dictionary<string, string>
            {
                ["ffmpegPath"] = "ffmpeg.exe", ["outputPath"] = "movie.mp4",
                ["maximumFrames"] = "10000", ["replayLoadedMovie"] = "true"
            };
            if (endFrame != null) fields["endMovieFrame"] = endFrame;
            return fields;
        }

        private static AutomationCommandEnvelope Command(string? endFrame) => Command(Arguments(endFrame));
        private static AutomationCommandEnvelope Command(IReadOnlyDictionary<string, string> fields)
            => new("request-test", "idempotency-test", "video-arguments-client", "video-arguments-session", Hash,
                AutomationCommandIds.StartVideoExport, AutomationScope.ControlPlayback, "lease-test", "Paused", 1400,
                IpcPayloadCodec.Serialize(fields));

        private static string? Validate(string method, AutomationCommandEnvelope command)
            => (string?)typeof(AutomationBroker).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { command });

        private static async Task<AutomationCommandEnvelope> CaptureCommandAsync(
            Func<IAutomationClient, CancellationToken, Task<AutomationResultEnvelope>> send)
        {
            var pipeName = "HollowKnightTAS.VideoArguments." + Guid.NewGuid().ToString("N");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var wire = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), wire.ConnectAsync(timeout.Token));
            await using var client = new AutomationClient();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            // Attach an isolated test pipe so assertions inspect the actual SDK wire envelope.
            typeof(AutomationClient).GetField("pipe", flags)!.SetValue(client, wire);
            typeof(AutomationClient).GetField("clientId", flags)!.SetValue(client, "video-arguments-client");
            typeof(AutomationClient).GetField("sessionId", flags)!.SetValue(client, "video-arguments-session");
            typeof(AutomationClient).GetField("manifestSha256", flags)!.SetValue(client, Hash);
            var pending = send(client, timeout.Token);
            var envelope = await IpcCodec.ReadFrameAsync(server, timeout.Token);
            Assert.IsTrue(AutomationCommandEnvelope.TryParse(envelope.PayloadUtf8, out var command, out _, out var error), error);
            var result = new AutomationResultEnvelope(command!.RequestId, true, "Ok", "test", command.SessionId,
                command.ManifestSha256, 1400, IpcPayloadCodec.Serialize(new Dictionary<string, string>()));
            await IpcCodec.WriteFrameAsync(server, new IpcEnvelope(AutomationProtocol.Version, envelope.SessionId,
                envelope.Sequence, AutomationProtocol.Result, result.ToPayload()), timeout.Token);
            Assert.IsTrue((await pending).Success);
            return command;
        }
    }
}
