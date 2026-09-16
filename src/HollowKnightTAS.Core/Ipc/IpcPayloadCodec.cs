using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using HollowKnightTAS.Core.Serialization;

namespace HollowKnightTAS.Core.Ipc
{
    public sealed class IpcPayloadDecodeResult
    {
        internal IpcPayloadDecodeResult(
            bool success,
            IReadOnlyDictionary<string, string>? fields,
            string errorCode,
            string error)
        {
            Success = success;
            Fields = fields;
            ErrorCode = errorCode;
            Error = error;
        }

        public bool Success { get; }
        public IReadOnlyDictionary<string, string>? Fields { get; }
        public string ErrorCode { get; }
        public string Error { get; }
    }

    public static class IpcPayloadCodec
    {
        public const int MaximumFieldCount = 128;
        public const int MaximumFieldValueCharacters = 900000;

        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(false, true);

        public static byte[] Serialize(
            IReadOnlyDictionary<string, string> fields)
        {
            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }

            if (fields.Count > MaximumFieldCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fields),
                    "IPC payload contains too many fields.");
            }

            var builder = new StringBuilder(
                Math.Min(
                    IpcEnvelope.MaximumMessageBytes,
                    64 + fields.Count * 64));
            builder.Append('{');
            var first = true;
            foreach (var pair in fields.OrderBy(
                         item => item.Key,
                         StringComparer.Ordinal))
            {
                ValidateField(pair.Key, pair.Value);
                if (!first)
                {
                    builder.Append(',');
                }

                CanonicalJsonWriter.AppendString(builder, pair.Key);
                builder.Append(':');
                CanonicalJsonWriter.AppendString(builder, pair.Value);
                first = false;
            }

            builder.Append('}');
            var result = StrictUtf8.GetBytes(builder.ToString());
            if (result.Length > IpcEnvelope.MaximumMessageBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fields),
                    "IPC payload is too large.");
            }

            return result;
        }

        public static IpcPayloadDecodeResult TryDeserialize(
            byte[] payloadUtf8)
        {
            if (payloadUtf8 == null)
            {
                return Fail("NullPayload", "Payload is null.");
            }

            if (payloadUtf8.Length > IpcEnvelope.MaximumMessageBytes)
            {
                return Fail("PayloadTooLarge", "Payload exceeds 1 MiB.");
            }

            string json;
            try
            {
                json = StrictUtf8.GetString(payloadUtf8);
            }
            catch (DecoderFallbackException exception)
            {
                return Fail("MalformedUtf8", exception.Message);
            }

            var reader = new StrictJsonReader(json);
            if (!reader.TryReadStringMap(
                    MaximumFieldCount,
                    MaximumFieldValueCharacters,
                    out var fields,
                    out var parseError)
                || fields == null)
            {
                var code = parseError.IndexOf(
                               "duplicate",
                               StringComparison.OrdinalIgnoreCase)
                           >= 0
                    ? "DuplicateField"
                    : parseError.IndexOf(
                          "string",
                          StringComparison.OrdinalIgnoreCase)
                      >= 0
                      ? "InvalidFieldType"
                      : "MalformedJson";
                return Fail(code, parseError);
            }

            if (!reader.IsAtEnd)
            {
                return Fail(
                    "TrailingContent",
                    "Payload contains trailing JSON content.");
            }

            byte[] canonical;
            try
            {
                canonical = Serialize(fields);
            }
            catch (Exception exception)
            {
                return Fail("InvalidPayload", exception.Message);
            }

            if (!ByteArraysEqual(canonical, payloadUtf8))
            {
                return Fail(
                    "NonCanonical",
                    "Payload JSON is not canonical.");
            }

            return new IpcPayloadDecodeResult(
                true,
                new ReadOnlyDictionary<string, string>(fields),
                string.Empty,
                string.Empty);
        }

        private static void ValidateField(string name, string value)
        {
            if (!IpcIdentifier.IsValid(name, 64))
            {
                throw new ArgumentException(
                    "IPC payload field name is invalid.",
                    nameof(name));
            }

            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value.Length > MaximumFieldValueCharacters)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "IPC payload field value is too large.");
            }
        }

        private static IpcPayloadDecodeResult Fail(
            string code,
            string error)
        {
            return new IpcPayloadDecodeResult(
                false,
                null,
                code,
                error);
        }

        internal static bool ByteArraysEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            var difference = 0;
            for (var index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
        }
    }
}
