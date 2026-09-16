using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HollowKnightTAS.Core.Ipc;

namespace HollowKnightTAS.Core.Automation
{
    public sealed class AutomationCommandEnvelope
    {
        private readonly byte[] canonicalArgumentsUtf8;

        public AutomationCommandEnvelope(
            string requestId,
            string idempotencyKey,
            string clientId,
            string sessionId,
            string manifestSha256,
            string commandId,
            string requiredScope,
            string leaseId,
            string expectedRuntimeMode,
            long? expectedMovieTick,
            byte[] canonicalArgumentsUtf8)
        {
            RequireIdentifier(requestId, 128, nameof(requestId));
            RequireIdentifier(
                idempotencyKey,
                128,
                nameof(idempotencyKey));
            RequireIdentifier(clientId, 128, nameof(clientId));
            RequireIdentifier(sessionId, 128, nameof(sessionId));
            if (!IsLowerSha256(manifestSha256))
            {
                throw new ArgumentException(
                    "Manifest hash must be lowercase SHA-256.",
                    nameof(manifestSha256));
            }

            if (!AutomationCommandIds.IsKnown(commandId))
            {
                throw new ArgumentException(
                    "Automation command is not registered.",
                    nameof(commandId));
            }

            if (!AutomationScope.IsKnown(requiredScope))
            {
                throw new ArgumentException(
                    "Automation scope is not registered.",
                    nameof(requiredScope));
            }

            if (!string.IsNullOrEmpty(leaseId))
            {
                RequireIdentifier(leaseId, 128, nameof(leaseId));
            }

            if (!string.IsNullOrEmpty(expectedRuntimeMode)
                && !IpcIdentifier.IsValid(expectedRuntimeMode, 64))
            {
                throw new ArgumentException(
                    "Expected Runtime mode is invalid.",
                    nameof(expectedRuntimeMode));
            }

            if (expectedMovieTick < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(expectedMovieTick));
            }

            var arguments = canonicalArgumentsUtf8
                            ?? throw new ArgumentNullException(
                                nameof(canonicalArgumentsUtf8));
            var decoded = IpcPayloadCodec.TryDeserialize(arguments);
            if (!decoded.Success)
            {
                throw new ArgumentException(
                    "Arguments must be a canonical flat string map: "
                    + decoded.ErrorCode,
                    nameof(canonicalArgumentsUtf8));
            }

            var canonical = IpcPayloadCodec.Serialize(decoded.Fields!);
            if (!IpcPayloadCodec.ByteArraysEqual(
                    arguments,
                    canonical))
            {
                throw new ArgumentException(
                    "Arguments are not canonical.",
                    nameof(canonicalArgumentsUtf8));
            }

            RequestId = requestId;
            IdempotencyKey = idempotencyKey;
            ClientId = clientId;
            SessionId = sessionId;
            ManifestSha256 = manifestSha256;
            CommandId = commandId;
            RequiredScope = requiredScope;
            LeaseId = leaseId ?? string.Empty;
            ExpectedRuntimeMode = expectedRuntimeMode ?? string.Empty;
            ExpectedMovieTick = expectedMovieTick;
            this.canonicalArgumentsUtf8 = (byte[])arguments.Clone();
        }

        public int SchemaVersion => AutomationProtocol.Version;
        public string RequestId { get; }
        public string IdempotencyKey { get; }
        public string ClientId { get; }
        public string SessionId { get; }
        public string ManifestSha256 { get; }
        public string CommandId { get; }
        public string RequiredScope { get; }
        public string LeaseId { get; }
        public string ExpectedRuntimeMode { get; }
        public long? ExpectedMovieTick { get; }
        public byte[] CanonicalArgumentsUtf8 =>
            (byte[])canonicalArgumentsUtf8.Clone();

        public IReadOnlyDictionary<string, string> Arguments =>
            IpcPayloadCodec.TryDeserialize(canonicalArgumentsUtf8)
                .Fields!;

        public byte[] ToPayload()
        {
            return IpcPayloadCodec.Serialize(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["argumentsBase64"] =
                        Convert.ToBase64String(canonicalArgumentsUtf8),
                    ["clientId"] = ClientId,
                    ["commandId"] = CommandId,
                    ["expectedMovieTick"] =
                        ExpectedMovieTick?.ToString(
                            CultureInfo.InvariantCulture)
                        ?? string.Empty,
                    ["expectedRuntimeMode"] = ExpectedRuntimeMode,
                    ["idempotencyKey"] = IdempotencyKey,
                    ["leaseId"] = LeaseId,
                    ["manifestSha256"] = ManifestSha256,
                    ["requestId"] = RequestId,
                    ["requiredScope"] = RequiredScope,
                    ["schemaVersion"] =
                        SchemaVersion.ToString(
                            CultureInfo.InvariantCulture),
                    ["sessionId"] = SessionId
                });
        }

        public static bool TryParse(
            byte[] payload,
            out AutomationCommandEnvelope? command,
            out string errorCode,
            out string error)
        {
            command = null;
            var decoded = IpcPayloadCodec.TryDeserialize(payload);
            if (!decoded.Success || decoded.Fields == null)
            {
                errorCode = decoded.ErrorCode;
                error = decoded.Error;
                return false;
            }

            var expected = new[]
            {
                "argumentsBase64",
                "clientId",
                "commandId",
                "expectedMovieTick",
                "expectedRuntimeMode",
                "idempotencyKey",
                "leaseId",
                "manifestSha256",
                "requestId",
                "requiredScope",
                "schemaVersion",
                "sessionId"
            };
            if (decoded.Fields.Count != expected.Length
                || expected.Any(
                    key => !decoded.Fields.ContainsKey(key)))
            {
                errorCode = "InvalidShape";
                error = "Automation command has unknown or missing fields.";
                return false;
            }

            if (!int.TryParse(
                    decoded.Fields["schemaVersion"],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var schema)
                || schema != AutomationProtocol.Version)
            {
                errorCode = "UnsupportedSchema";
                error = "Automation command schema is unsupported.";
                return false;
            }

            long? tick = null;
            if (decoded.Fields["expectedMovieTick"].Length > 0)
            {
                if (!long.TryParse(
                        decoded.Fields["expectedMovieTick"],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var parsedTick)
                    || parsedTick < 0)
                {
                    errorCode = "InvalidExpectedTick";
                    error = "Expected movie tick is invalid.";
                    return false;
                }

                tick = parsedTick;
            }

            byte[] arguments;
            try
            {
                arguments = Convert.FromBase64String(
                    decoded.Fields["argumentsBase64"]);
            }
            catch (FormatException)
            {
                errorCode = "InvalidArguments";
                error = "Arguments are not valid base64.";
                return false;
            }

            try
            {
                command = new AutomationCommandEnvelope(
                    decoded.Fields["requestId"],
                    decoded.Fields["idempotencyKey"],
                    decoded.Fields["clientId"],
                    decoded.Fields["sessionId"],
                    decoded.Fields["manifestSha256"],
                    decoded.Fields["commandId"],
                    decoded.Fields["requiredScope"],
                    decoded.Fields["leaseId"],
                    decoded.Fields["expectedRuntimeMode"],
                    tick,
                    arguments);
                errorCode = string.Empty;
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                errorCode = "InvalidCommand";
                error = exception.Message;
                return false;
            }
        }

        private static void RequireIdentifier(
            string value,
            int maximumLength,
            string parameterName)
        {
            if (!IpcIdentifier.IsValid(value, maximumLength))
            {
                throw new ArgumentException(
                    "Value is not a safe identifier.",
                    parameterName);
            }
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
