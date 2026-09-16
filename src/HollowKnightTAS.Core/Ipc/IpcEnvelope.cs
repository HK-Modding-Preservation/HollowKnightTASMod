using System;

namespace HollowKnightTAS.Core.Ipc
{
    public sealed class IpcEnvelope
    {
        public const int MaximumMessageBytes = 1024 * 1024;

        private readonly byte[] payloadUtf8;

        public IpcEnvelope(
            int protocolVersion,
            string sessionId,
            long sequence,
            string messageType,
            byte[] payloadUtf8)
        {
            if (protocolVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(protocolVersion));
            }

            if (!IpcIdentifier.IsValid(sessionId, 128))
            {
                throw new ArgumentException(
                    "Session ID must be a safe identifier.",
                    nameof(sessionId));
            }

            if (sequence < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence));
            }

            if (!IpcIdentifier.IsValid(messageType, 64))
            {
                throw new ArgumentException(
                    "Message type must be a safe identifier.",
                    nameof(messageType));
            }

            if (payloadUtf8 == null)
            {
                throw new ArgumentNullException(nameof(payloadUtf8));
            }

            if (payloadUtf8.Length > MaximumMessageBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(payloadUtf8),
                    "IPC payload is too large.");
            }

            ProtocolVersion = protocolVersion;
            SessionId = sessionId;
            Sequence = sequence;
            MessageType = messageType;
            this.payloadUtf8 = (byte[])payloadUtf8.Clone();
        }

        public int ProtocolVersion { get; }
        public string SessionId { get; }
        public long Sequence { get; }
        public string MessageType { get; }
        public byte[] PayloadUtf8 => (byte[])payloadUtf8.Clone();

        internal byte[] UnsafePayloadUtf8 => payloadUtf8;
    }

    public static class IpcIdentifier
    {
        public static bool IsValid(string? value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value)
                || value!.Length > maximumLength)
            {
                return false;
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!char.IsLetterOrDigit(character)
                    && character != '.'
                    && character != '-'
                    && character != '_'
                    && character != ':')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
