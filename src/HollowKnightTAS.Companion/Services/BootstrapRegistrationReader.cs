using System;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Core.Ipc;

namespace HollowKnightTAS.Companion.Services
{
    public static class BootstrapRegistrationReader
    {
        public static async Task<CompanionSessionRegistration?> TryReadAsync(
            string[] arguments,
            CancellationToken cancellationToken)
        {
            var handleArgument = arguments.FirstOrDefault(
                value => value.StartsWith(
                    "--bootstrap-handle=",
                    StringComparison.Ordinal));
            var pipeArgument = arguments.FirstOrDefault(
                value => value.StartsWith(
                    "--bootstrap-pipe=",
                    StringComparison.Ordinal));
            if (handleArgument != null
                && pipeArgument != null)
            {
                throw new InvalidOperationException(
                    "Only one bootstrap channel is allowed.");
            }

            if (handleArgument == null
                && pipeArgument == null)
            {
                return null;
            }

            using (var timeout =
                   CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                if (handleArgument != null)
                {
                    var handle = handleArgument.Substring(
                        "--bootstrap-handle=".Length);
                    if (string.IsNullOrWhiteSpace(handle)
                        || handle.Length > 128)
                    {
                        throw new InvalidOperationException(
                            "Bootstrap handle is invalid.");
                    }

                    using (var pipe =
                           new AnonymousPipeClientStream(
                               PipeDirection.In,
                               handle))
                    {
                        return await ReadRegistrationAsync(
                            pipe,
                            timeout.Token);
                    }
                }

                var pipeName = pipeArgument!.Substring(
                    "--bootstrap-pipe=".Length);
                if (!IpcIdentifier.IsValid(
                        pipeName,
                        240))
                {
                    throw new InvalidOperationException(
                        "Bootstrap pipe name is invalid.");
                }

                using (var pipe =
                       new NamedPipeClientStream(
                           ".",
                           pipeName,
                           PipeDirection.In,
                           PipeOptions.Asynchronous
                           | PipeOptions.CurrentUserOnly))
                {
                    await pipe.ConnectAsync(timeout.Token);
                    return await ReadRegistrationAsync(
                        pipe,
                        timeout.Token);
                }
            }
        }

        private static async Task<CompanionSessionRegistration>
            ReadRegistrationAsync(
                PipeStream pipe,
                CancellationToken cancellationToken)
        {
            var envelope = await IpcCodec.ReadFrameAsync(
                pipe,
                cancellationToken);
            var error = "Envelope binding is invalid.";
            if (!string.Equals(
                    envelope.MessageType,
                    IpcMessageTypes.RegisterSession,
                    StringComparison.Ordinal)
                || envelope.Sequence != 0
                || !CompanionSessionRegistration.TryParse(
                    envelope.PayloadUtf8,
                    out var registration,
                    out error)
                || registration == null
                || !string.Equals(
                    envelope.SessionId,
                    registration.SessionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Bootstrap registration is invalid: "
                    + error);
            }

            return registration;
        }
    }
}
