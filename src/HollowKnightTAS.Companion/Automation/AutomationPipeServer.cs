using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Core.Automation;
using HollowKnightTAS.Core.Ipc;

namespace HollowKnightTAS.Companion.Automation
{
    public sealed class AutomationPipeServer : IDisposable
    {
        private readonly string pipeName;
        private readonly AutomationBroker broker;
        private readonly CancellationTokenSource cancellation =
            new CancellationTokenSource();
        private Task? acceptLoop;

        public AutomationPipeServer(
            string pipeName,
            AutomationBroker broker,
            AutomationSessionAuthenticator authenticator)
        {
            this.pipeName = pipeName;
            this.broker = broker;
            Authenticator = authenticator;
        }

        public AutomationSessionAuthenticator Authenticator { get; }

        public void Start()
        {
            if (acceptLoop == null)
            {
                acceptLoop = Task.Run(
                    () => AcceptLoopAsync(cancellation.Token));
            }
        }

        public void Dispose()
        {
            cancellation.Cancel();
            try
            {
                acceptLoop?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }

            cancellation.Dispose();
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    server = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.InOut,
                        8,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous
                        | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(token);
                    var connected = server;
                    server = null;
                    _ = Task.Run(
                        () => HandleConnectionAsync(
                            connected,
                            token),
                        token);
                }
                catch (OperationCanceledException)
                    when (token.IsCancellationRequested)
                {
                    server?.Dispose();
                    return;
                }
                catch (IOException)
                {
                    server?.Dispose();
                    await DelayAfterFaultAsync(token);
                }
                catch (UnauthorizedAccessException)
                {
                    server?.Dispose();
                    return;
                }
            }
        }

        private async Task HandleConnectionAsync(
            NamedPipeServerStream stream,
            CancellationToken serverToken)
        {
            var connectionId = Guid.NewGuid().ToString("N");
            try
            {
                using (stream)
                using (var linked =
                       CancellationTokenSource
                           .CreateLinkedTokenSource(serverToken))
                {
                    linked.CancelAfter(TimeSpan.FromSeconds(10));
                    var hello = await IpcCodec.ReadFrameAsync(
                        stream,
                        linked.Token);
                    var bootstrap = broker.Bootstrap;
                    if (bootstrap == null)
                    {
                        return;
                    }

                    var authentication =
                        Authenticator.Authenticate(
                            hello,
                            bootstrap.SessionId,
                            bootstrap.ManifestSha256);
                    if (!authentication.Success
                        || authentication.ServerNonce == null)
                    {
                        return;
                    }

                    await IpcCodec.WriteFrameAsync(
                        stream,
                        new IpcEnvelope(
                            AutomationProtocol.Version,
                            bootstrap.SessionId,
                            0,
                            AutomationProtocol.HelloAck,
                            IpcPayloadCodec.Serialize(
                                new Dictionary<string, string>(
                                    StringComparer.Ordinal)
                                {
                                    ["clientId"] =
                                        authentication.ClientId,
                                    ["manifestSha256"] =
                                        bootstrap.ManifestSha256,
                                    ["mode"] =
                                        bootstrap.Mode.ToString(),
                                    ["product"] =
                                        AutomationProtocol.Product,
                                    ["schemaVersion"] = "1",
                                    ["serverNonce"] =
                                        Convert.ToBase64String(
                                            authentication
                                                .ServerNonce),
                                    ["sessionId"] =
                                        bootstrap.SessionId
                                })),
                        linked.Token);
                    linked.CancelAfter(Timeout.InfiniteTimeSpan);
                    var sequence = new IpcSequenceValidator(0);
                    while (!linked.IsCancellationRequested
                           && stream.IsConnected)
                    {
                        var request = await IpcCodec.ReadFrameAsync(
                            stream,
                            linked.Token);
                        if (request.ProtocolVersion
                            != AutomationProtocol.Version
                            || request.SessionId
                            != bootstrap.SessionId
                            || request.MessageType
                            != AutomationProtocol.Command
                            || !sequence.TryAccept(
                                request.Sequence,
                                out _))
                        {
                            return;
                        }

                        AutomationResultEnvelope result;
                        if (!AutomationCommandEnvelope.TryParse(
                                request.PayloadUtf8,
                                out var command,
                                out var errorCode,
                                out var error)
                            || command == null)
                        {
                            result = new AutomationResultEnvelope(
                                "invalid-"
                                + Guid.NewGuid().ToString("N"),
                                false,
                                IpcIdentifier.IsValid(
                                    errorCode,
                                    64)
                                    ? errorCode
                                    : "InvalidCommand",
                                error,
                                bootstrap.SessionId,
                                bootstrap.ManifestSha256,
                                -1,
                                IpcPayloadCodec.Serialize(
                                    new Dictionary<string, string>(
                                        StringComparer.Ordinal)));
                        }
                        else
                        {
                            result = await broker.ExecuteAsync(
                                authentication.ClientId,
                                connectionId,
                                command,
                                linked.Token);
                        }

                        await IpcCodec.WriteFrameAsync(
                            stream,
                            new IpcEnvelope(
                                AutomationProtocol.Version,
                                bootstrap.SessionId,
                                request.Sequence,
                                AutomationProtocol.Result,
                                result.ToPayload()),
                            linked.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (IpcProtocolException)
            {
            }
            finally
            {
                broker.ReleaseConnection(connectionId);
            }
        }

        private static async Task DelayAfterFaultAsync(
            CancellationToken token)
        {
            try
            {
                await Task.Delay(100, token);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
