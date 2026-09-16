using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Core.Serialization;

namespace HollowKnightTAS.Core.Ipc
{
    public sealed class IpcDecodeResult
    {
        internal IpcDecodeResult(
            bool success,
            IpcEnvelope? envelope,
            string errorCode,
            string error)
        {
            Success = success;
            Envelope = envelope;
            ErrorCode = errorCode;
            Error = error;
        }

        public bool Success { get; }
        public IpcEnvelope? Envelope { get; }
        public string ErrorCode { get; }
        public string Error { get; }
    }

    public sealed class IpcProtocolException : Exception
    {
        public IpcProtocolException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }

    public static class IpcCodec
    {
        public const int FrameHeaderBytes = 4;

        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(false, true);

        public static byte[] SerializeEnvelope(IpcEnvelope value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var payloadResult = IpcPayloadCodec.TryDeserialize(
                value.UnsafePayloadUtf8);
            if (!payloadResult.Success)
            {
                throw new ArgumentException(
                    "IPC envelope payload is invalid: "
                    + payloadResult.ErrorCode
                    + ": "
                    + payloadResult.Error,
                    nameof(value));
            }

            var builder = new StringBuilder(
                192 + value.UnsafePayloadUtf8.Length);
            builder.Append("{\"protocolVersion\":");
            builder.Append(
                value.ProtocolVersion.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"sessionId\":");
            CanonicalJsonWriter.AppendString(builder, value.SessionId);
            builder.Append(",\"sequence\":");
            builder.Append(
                value.Sequence.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"messageType\":");
            CanonicalJsonWriter.AppendString(builder, value.MessageType);
            builder.Append(",\"payload\":");
            builder.Append(StrictUtf8.GetString(value.UnsafePayloadUtf8));
            builder.Append('}');
            var result = StrictUtf8.GetBytes(builder.ToString());
            if (result.Length > IpcEnvelope.MaximumMessageBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "IPC envelope exceeds 1 MiB.");
            }

            return result;
        }

        public static IpcDecodeResult TryDeserializeEnvelope(
            byte[] envelopeUtf8)
        {
            if (envelopeUtf8 == null)
            {
                return Fail("NullEnvelope", "Envelope is null.");
            }

            if (envelopeUtf8.Length == 0
                || envelopeUtf8.Length > IpcEnvelope.MaximumMessageBytes)
            {
                return Fail(
                    "InvalidLength",
                    "Envelope length must be in [1, 1048576].");
            }

            string json;
            try
            {
                json = StrictUtf8.GetString(envelopeUtf8);
            }
            catch (DecoderFallbackException exception)
            {
                return Fail("MalformedUtf8", exception.Message);
            }

            var reader = new StrictJsonReader(json);
            if (!reader.TryReadObjectStart(out var parseError)
                || !reader.TryReadPropertyName(
                    "protocolVersion",
                    out parseError)
                || !reader.TryReadInt64(
                    out var protocolLong,
                    out parseError)
                || !reader.TryReadComma(out parseError)
                || !reader.TryReadPropertyName(
                    "sessionId",
                    out parseError)
                || !reader.TryReadString(
                    out var sessionId,
                    out parseError)
                || !reader.TryReadComma(out parseError)
                || !reader.TryReadPropertyName(
                    "sequence",
                    out parseError)
                || !reader.TryReadInt64(
                    out var sequence,
                    out parseError)
                || !reader.TryReadComma(out parseError)
                || !reader.TryReadPropertyName(
                    "messageType",
                    out parseError)
                || !reader.TryReadString(
                    out var messageType,
                    out parseError)
                || !reader.TryReadComma(out parseError)
                || !reader.TryReadPropertyName(
                    "payload",
                    out parseError)
                || !reader.TryReadStringMap(
                    IpcPayloadCodec.MaximumFieldCount,
                    IpcPayloadCodec.MaximumFieldValueCharacters,
                    out var payloadFields,
                    out parseError)
                || payloadFields == null
                || !reader.TryReadObjectEnd(out parseError))
            {
                return Fail("InvalidShape", parseError);
            }

            if (!reader.IsAtEnd)
            {
                return Fail(
                    "TrailingContent",
                    "Envelope contains trailing JSON content.");
            }

            if (protocolLong <= 0 || protocolLong > int.MaxValue)
            {
                return Fail(
                    "InvalidProtocol",
                    "protocolVersion must be a positive Int32.");
            }

            var protocolVersion = (int)protocolLong;
            if (!IpcIdentifier.IsValid(sessionId, 128))
            {
                return Fail(
                    "InvalidSession",
                    "sessionId is invalid.");
            }

            if (sequence < 0)
            {
                return Fail(
                    "InvalidSequence",
                    "sequence must be a non-negative Int64.");
            }

            if (!IpcIdentifier.IsValid(messageType, 64))
            {
                return Fail(
                    "InvalidMessageType",
                    "messageType is invalid.");
            }

            byte[] payload;
            IpcEnvelope envelope;
            byte[] canonical;
            try
            {
                payload = IpcPayloadCodec.Serialize(payloadFields);
                envelope = new IpcEnvelope(
                    protocolVersion,
                    sessionId,
                    sequence,
                    messageType,
                    payload);
                canonical = SerializeEnvelope(envelope);
            }
            catch (Exception exception)
            {
                return Fail("InvalidEnvelope", exception.Message);
            }

            if (!IpcPayloadCodec.ByteArraysEqual(
                    canonical,
                    envelopeUtf8))
            {
                return Fail(
                    "NonCanonical",
                    "Envelope JSON is not canonical.");
            }

            return new IpcDecodeResult(
                true,
                envelope,
                string.Empty,
                string.Empty);
        }

        public static byte[] EncodeFrame(IpcEnvelope envelope)
        {
            var body = SerializeEnvelope(envelope);
            var result = new byte[FrameHeaderBytes + body.Length];
            WriteUInt32BigEndian(
                result,
                0,
                checked((uint)body.Length));
            Buffer.BlockCopy(
                body,
                0,
                result,
                FrameHeaderBytes,
                body.Length);
            return result;
        }

        public static async Task WriteFrameAsync(
            Stream stream,
            IpcEnvelope envelope,
            CancellationToken cancellationToken)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            var frame = EncodeFrame(envelope);
            await stream.WriteAsync(
                    frame,
                    0,
                    frame.Length,
                    cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public static async Task<IpcEnvelope> ReadFrameAsync(
            Stream stream,
            CancellationToken cancellationToken)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            var header = new byte[FrameHeaderBytes];
            await ReadExactAsync(
                    stream,
                    header,
                    header.Length,
                    cancellationToken)
                .ConfigureAwait(false);
            var length = ReadUInt32BigEndian(header, 0);
            if (length == 0
                || length > IpcEnvelope.MaximumMessageBytes)
            {
                throw new IpcProtocolException(
                    "InvalidFrameLength",
                    "IPC frame length must be in [1, 1048576].");
            }

            var body = new byte[checked((int)length)];
            await ReadExactAsync(
                    stream,
                    body,
                    body.Length,
                    cancellationToken)
                .ConfigureAwait(false);
            var result = TryDeserializeEnvelope(body);
            if (!result.Success || result.Envelope == null)
            {
                throw new IpcProtocolException(
                    result.ErrorCode,
                    result.Error);
            }

            return result.Envelope;
        }

        private static async Task ReadExactAsync(
            Stream stream,
            byte[] buffer,
            int count,
            CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < count)
            {
                var read = await stream.ReadAsync(
                        buffer,
                        offset,
                        count - offset,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException(
                        "IPC stream ended inside a frame.");
                }

                offset += read;
            }
        }

        private static void WriteUInt32BigEndian(
            byte[] buffer,
            int offset,
            uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static uint ReadUInt32BigEndian(
            byte[] buffer,
            int offset)
        {
            return ((uint)buffer[offset] << 24)
                   | ((uint)buffer[offset + 1] << 16)
                   | ((uint)buffer[offset + 2] << 8)
                   | buffer[offset + 3];
        }

        private static IpcDecodeResult Fail(
            string code,
            string error)
        {
            return new IpcDecodeResult(
                false,
                null,
                code,
                error);
        }
    }
}
