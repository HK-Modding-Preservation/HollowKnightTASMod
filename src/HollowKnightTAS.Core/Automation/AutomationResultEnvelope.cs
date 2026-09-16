using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HollowKnightTAS.Core.Ipc;

namespace HollowKnightTAS.Core.Automation
{
    public sealed class AutomationResultEnvelope
    {
        private readonly byte[] canonicalDataUtf8;

        public AutomationResultEnvelope(
            string requestId,
            bool success,
            string resultCode,
            string detail,
            string sessionId,
            string manifestSha256,
            long acceptedAtMovieTick,
            byte[] canonicalDataUtf8)
        {
            if (!IpcIdentifier.IsValid(requestId, 128)
                || !IpcIdentifier.IsValid(resultCode, 64)
                || !IpcIdentifier.IsValid(sessionId, 128)
                || !IsLowerSha256(manifestSha256)
                || acceptedAtMovieTick < -1)
            {
                throw new ArgumentException(
                    "Automation result binding is invalid.");
            }

            var data = canonicalDataUtf8
                       ?? throw new ArgumentNullException(
                           nameof(canonicalDataUtf8));
            var decoded = IpcPayloadCodec.TryDeserialize(data);
            if (!decoded.Success
                || !IpcPayloadCodec.ByteArraysEqual(
                    data,
                    IpcPayloadCodec.Serialize(decoded.Fields!)))
            {
                throw new ArgumentException(
                    "Result data is not a canonical flat string map.",
                    nameof(canonicalDataUtf8));
            }

            RequestId = requestId;
            Success = success;
            ResultCode = resultCode;
            Detail = Sanitize(detail);
            SessionId = sessionId;
            ManifestSha256 = manifestSha256;
            AcceptedAtMovieTick = acceptedAtMovieTick;
            this.canonicalDataUtf8 = (byte[])data.Clone();
        }

        public int SchemaVersion => AutomationProtocol.Version;
        public string RequestId { get; }
        public bool Success { get; }
        public string ResultCode { get; }
        public string Detail { get; }
        public string SessionId { get; }
        public string ManifestSha256 { get; }
        public long AcceptedAtMovieTick { get; }
        public byte[] CanonicalDataUtf8 =>
            (byte[])canonicalDataUtf8.Clone();
        public IReadOnlyDictionary<string, string> Data =>
            IpcPayloadCodec.TryDeserialize(canonicalDataUtf8)
                .Fields!;

        public byte[] ToPayload()
        {
            return IpcPayloadCodec.Serialize(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["acceptedAtMovieTick"] =
                        AcceptedAtMovieTick.ToString(
                            CultureInfo.InvariantCulture),
                    ["dataBase64"] =
                        Convert.ToBase64String(canonicalDataUtf8),
                    ["detail"] = Detail,
                    ["manifestSha256"] = ManifestSha256,
                    ["requestId"] = RequestId,
                    ["resultCode"] = ResultCode,
                    ["schemaVersion"] =
                        SchemaVersion.ToString(
                            CultureInfo.InvariantCulture),
                    ["sessionId"] = SessionId,
                    ["success"] = Success ? "true" : "false"
                });
        }

        public static bool TryParse(
            byte[] payload,
            out AutomationResultEnvelope? result,
            out string error)
        {
            result = null;
            var decoded = IpcPayloadCodec.TryDeserialize(payload);
            var required = new[]
            {
                "acceptedAtMovieTick",
                "dataBase64",
                "detail",
                "manifestSha256",
                "requestId",
                "resultCode",
                "schemaVersion",
                "sessionId",
                "success"
            };
            if (!decoded.Success
                || decoded.Fields == null
                || decoded.Fields.Count != required.Length
                || required.Any(
                    key => !decoded.Fields.ContainsKey(key)))
            {
                error = "Automation result shape is invalid.";
                return false;
            }

            if (decoded.Fields["schemaVersion"] != "1"
                || !long.TryParse(
                    decoded.Fields["acceptedAtMovieTick"],
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out var tick)
                || tick < -1)
            {
                error = "Automation result metadata is invalid.";
                return false;
            }

            var successText = decoded.Fields["success"];
            var success = successText == "true";
            if (!success && successText != "false")
            {
                error = "Automation result success flag is invalid.";
                return false;
            }

            try
            {
                result = new AutomationResultEnvelope(
                    decoded.Fields["requestId"],
                    success,
                    decoded.Fields["resultCode"],
                    decoded.Fields["detail"],
                    decoded.Fields["sessionId"],
                    decoded.Fields["manifestSha256"],
                    tick,
                    Convert.FromBase64String(
                        decoded.Fields["dataBase64"]));
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static string Sanitize(string? value)
        {
            var result = (value ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ');
            return result.Substring(0, Math.Min(512, result.Length));
        }

        private static bool IsLowerSha256(string? value)
        {
            return value != null
                   && value.Length == 64
                   && value.All(
                       character =>
                           character >= '0'
                           && character <= '9'
                           || character >= 'a'
                           && character <= 'f');
        }
    }
}
