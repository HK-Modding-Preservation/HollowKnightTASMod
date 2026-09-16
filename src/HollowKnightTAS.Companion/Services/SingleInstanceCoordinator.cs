using System;
using System.Globalization;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Core.Ipc;

namespace HollowKnightTAS.Companion.Services
{
    public sealed class SingleInstanceCoordinator : IDisposable
    {
        private readonly Mutex mutex;
        private bool disposed;

        public SingleInstanceCoordinator()
        {
            var identity =
                CompanionInstanceNames
                    .GetCurrentUserIdentity();
            MutexName =
                CompanionInstanceNames.CreateMutexName(identity);
            ControlPipeName =
                CompanionInstanceNames.CreateControlPipeName(identity);
            mutex = new Mutex(
                true,
                MutexName,
                out var createdNew);
            IsPrimary = createdNew;
        }

        public bool IsPrimary { get; }
        public string MutexName { get; }
        public string ControlPipeName { get; }

        public async Task<bool> ForwardRegistrationAsync(
            CompanionSessionRegistration registration,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using (var linked =
                   CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken))
            using (var client = new NamedPipeClientStream(
                       ".",
                       ControlPipeName,
                       PipeDirection.InOut,
                       PipeOptions.Asynchronous))
            {
                linked.CancelAfter(timeout);
                await client.ConnectAsync(linked.Token);
                await IpcCodec.WriteFrameAsync(
                    client,
                    new IpcEnvelope(
                        1,
                        registration.SessionId,
                        0,
                        IpcMessageTypes.RegisterSession,
                        registration.ToPayload()),
                    linked.Token);
                var response = await IpcCodec.ReadFrameAsync(
                    client,
                    linked.Token);
                var payload = IpcPayloadCodec.TryDeserialize(
                    response.PayloadUtf8);
                if (!string.Equals(
                        response.MessageType,
                        IpcMessageTypes.RegisterSessionAck,
                        StringComparison.Ordinal)
                    || response.Sequence != 0
                    || !string.Equals(
                        response.SessionId,
                        registration.SessionId,
                        StringComparison.Ordinal)
                    || !payload.Success
                    || payload.Fields == null
                    || payload.Fields.Count != 7
                    || !payload.Fields.TryGetValue(
                        "accepted",
                        out var accepted)
                    || !string.Equals(
                        accepted,
                        "true",
                        StringComparison.Ordinal)
                    || !payload.Fields.TryGetValue(
                        "product",
                        out var product)
                    || !string.Equals(
                        product,
                        CompanionProtocolMetadata.Product,
                        StringComparison.Ordinal)
                    || !payload.Fields.TryGetValue(
                        "companionInstanceId",
                        out var companionInstanceId)
                    || !IpcIdentifier.IsValid(
                        companionInstanceId,
                        128)
                    || !payload.Fields.TryGetValue(
                        "version",
                        out var version)
                    || string.IsNullOrWhiteSpace(version)
                    || !payload.Fields.TryGetValue(
                        "protocolMin",
                        out var protocolMinText)
                    || !payload.Fields.TryGetValue(
                        "protocolMax",
                        out var protocolMaxText)
                    || !int.TryParse(
                        protocolMinText,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var protocolMinimum)
                    || !int.TryParse(
                        protocolMaxText,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var protocolMaximum)
                    || protocolMinimum <= 0
                    || protocolMaximum < protocolMinimum)
                {
                    return false;
                }

                return registration.ProtocolRange.Intersects(
                    new ProtocolRange(
                        protocolMinimum,
                        protocolMaximum));
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (IsPrimary)
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }
            }

            mutex.Dispose();
        }
    }
}
