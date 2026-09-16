using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Core.Ipc;

namespace HollowKnightTAS.Companion.Services
{
    public sealed class ControlPipeServer : IDisposable
    {
        private readonly string pipeName;
        private readonly SessionRegistry sessions;
        private readonly Func<bool> canExitCompanion;
        private readonly CancellationTokenSource cancellation =
            new CancellationTokenSource();
        private Task? loop;

        public ControlPipeServer(
            string pipeName,
            SessionRegistry sessions,
            Func<bool>? canExitCompanion = null)
        {
            this.pipeName = pipeName;
            this.sessions = sessions;
            this.canExitCompanion = canExitCompanion
                                    ?? (() => true);
        }

        public event EventHandler? ExitRequested;

        public string PipeName => pipeName;

        public void Start()
        {
            if (loop != null)
            {
                return;
            }

            loop = Task.Run(() => RunAsync(cancellation.Token));
        }

        public void Dispose()
        {
            cancellation.Cancel();
            try
            {
                loop?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }

            cancellation.Dispose();
        }

        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(
                               pipeName,
                               PipeDirection.InOut,
                               1,
                               PipeTransmissionMode.Byte,
                               PipeOptions.Asynchronous
                               | PipeOptions.CurrentUserOnly))
                    {
                        await server.WaitForConnectionAsync(token);
                        await HandleAsync(server, token);
                    }
                }
                catch (OperationCanceledException)
                    when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (IOException)
                {
                    await Task.Delay(100, token);
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
            }
        }

        private async Task HandleAsync(
            Stream stream,
            CancellationToken token)
        {
            using (var timeout =
                   CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var request = await IpcCodec.ReadFrameAsync(
                    stream,
                    timeout.Token);
                var accepted = false;
                var requestExit = false;
                var detail = "Invalid control request.";
                var responseType = IpcMessageTypes.ControlAck;
                if (request.Sequence == 0
                    && CompanionSessionRegistration.TryParse(
                        request.PayloadUtf8,
                        out var registration,
                        out detail)
                    && registration != null
                    && string.Equals(
                        request.SessionId,
                        registration.SessionId,
                        StringComparison.Ordinal))
                {
                    if (string.Equals(
                            request.MessageType,
                            IpcMessageTypes.RegisterSession,
                            StringComparison.Ordinal))
                    {
                        responseType =
                            IpcMessageTypes.RegisterSessionAck;
                        accepted = await sessions.RegisterAsync(
                            registration,
                            timeout.Token);
                        detail = accepted
                            ? "Session registered."
                            : "Session connection failed.";
                    }
                    else if (string.Equals(
                                 request.MessageType,
                                 IpcMessageTypes.ShutdownSession,
                                 StringComparison.Ordinal))
                    {
                        accepted = sessions.Remove(registration);
                        detail = accepted
                            ? "Session removed."
                            : "Session authentication failed.";
                    }
                    else if (string.Equals(
                                 request.MessageType,
                                 IpcMessageTypes.RequestCompanionExit,
                                 StringComparison.Ordinal))
                    {
                        var removed = sessions.Remove(registration);
                        accepted = removed
                                   && sessions.ConnectedCount == 0
                                   && canExitCompanion();
                        detail = accepted
                            ? "Companion exit accepted."
                            : removed
                              && !canExitCompanion()
                                ? "Companion exit is deferred by an active cold restore."
                                : "Companion still owns another session "
                                  + "or authentication failed.";
                        requestExit = accepted;
                    }
                }

                await IpcCodec.WriteFrameAsync(
                    stream,
                    new IpcEnvelope(
                        1,
                        request.SessionId,
                        0,
                        responseType,
                        IpcPayloadCodec.Serialize(
                            new Dictionary<string, string>(
                                StringComparer.Ordinal)
                            {
                                ["accepted"] =
                                    accepted ? "true" : "false",
                                ["companionInstanceId"] =
                                    sessions.CompanionInstanceId,
                                ["detail"] = detail,
                                ["product"] =
                                    CompanionProtocolMetadata.Product,
                                ["protocolMax"] =
                                    CompanionProtocolMetadata
                                        .ProtocolMaximum
                                        .ToString(
                                            CultureInfo
                                                .InvariantCulture),
                                ["protocolMin"] =
                                    CompanionProtocolMetadata
                                        .ProtocolMinimum
                                        .ToString(
                                            CultureInfo
                                                .InvariantCulture),
                                ["version"] =
                                    CompanionProtocolMetadata.Version
                            })),
                    timeout.Token);
                if (requestExit)
                {
                    ExitRequested?.Invoke(
                        this,
                        EventArgs.Empty);
                }
            }
        }
    }
}
