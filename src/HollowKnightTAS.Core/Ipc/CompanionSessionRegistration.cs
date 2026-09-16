using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HollowKnightTAS.Core.Automation;
using HollowKnightTAS.Core.Cryptography;

namespace HollowKnightTAS.Core.Ipc
{
    public sealed class CompanionSessionRegistration
    {
        private readonly byte[] token;

        public CompanionSessionRegistration(
            string sessionId,
            int gameProcessId,
            long gameProcessStartTimeUtcTicks,
            string environmentManifestSha256,
            string runtimeAssemblySha256,
            string coreAssemblySha256,
            string pipeName,
            ProtocolRange protocolRange,
            bool nativeCapabilitiesRequested,
            byte[] token,
            AutomationMode automationMode =
                AutomationMode.ReadOnly,
            bool debugMutationEnabled = false)
        {
            if (!IpcIdentifier.IsValid(sessionId, 128))
            {
                throw new ArgumentException(
                    "Runtime session ID is invalid.",
                    nameof(sessionId));
            }

            if (gameProcessId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(gameProcessId));
            }

            if (gameProcessStartTimeUtcTicks <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(gameProcessStartTimeUtcTicks));
            }

            if (!IsLowerSha256(environmentManifestSha256)
                || !IsLowerSha256(runtimeAssemblySha256)
                || !IsLowerSha256(coreAssemblySha256))
            {
                throw new ArgumentException(
                    "Runtime registration hashes must be lowercase SHA-256.");
            }

            if (!IpcIdentifier.IsValid(pipeName, 240))
            {
                throw new ArgumentException(
                    "Runtime pipe name is invalid.",
                    nameof(pipeName));
            }

            if (token == null || token.Length != 32)
            {
                throw new ArgumentException(
                    "Session token must contain 32 bytes.",
                    nameof(token));
            }

            SessionId = sessionId;
            GameProcessId = gameProcessId;
            GameProcessStartTimeUtcTicks =
                gameProcessStartTimeUtcTicks;
            EnvironmentManifestSha256 =
                environmentManifestSha256;
            RuntimeAssemblySha256 = runtimeAssemblySha256;
            CoreAssemblySha256 = coreAssemblySha256;
            PipeName = pipeName;
            ProtocolRange = protocolRange;
            NativeCapabilitiesRequested =
                nativeCapabilitiesRequested;
            AutomationMode = automationMode;
            DebugMutationEnabled = debugMutationEnabled;
            this.token = (byte[])token.Clone();
        }

        public string SessionId { get; }
        public int GameProcessId { get; }
        public long GameProcessStartTimeUtcTicks { get; }
        public string EnvironmentManifestSha256 { get; }
        public string RuntimeAssemblySha256 { get; }
        public string CoreAssemblySha256 { get; }
        public string PipeName { get; }
        public ProtocolRange ProtocolRange { get; }
        public bool NativeCapabilitiesRequested { get; }
        public AutomationMode AutomationMode { get; }
        public bool DebugMutationEnabled { get; }
        public byte[] Token => (byte[])token.Clone();

        public byte[] ToPayload()
        {
            return IpcPayloadCodec.Serialize(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["gameProcessId"] =
                        GameProcessId.ToString(
                            CultureInfo.InvariantCulture),
                    ["gameProcessStartTimeUtcTicks"] =
                        GameProcessStartTimeUtcTicks.ToString(
                            CultureInfo.InvariantCulture),
                    ["environmentManifestSha256"] =
                        EnvironmentManifestSha256,
                    ["runtimeAssemblySha256"] =
                        RuntimeAssemblySha256,
                    ["coreAssemblySha256"] =
                        CoreAssemblySha256,
                    ["nativeCapabilitiesRequested"] =
                        NativeCapabilitiesRequested
                            ? "true"
                            : "false",
                    ["automationMode"] =
                        AutomationMode.ToString(),
                    ["debugMutationEnabled"] =
                        DebugMutationEnabled ? "true" : "false",
                    ["pipeName"] = PipeName,
                    ["protocolMax"] =
                        ProtocolRange.Maximum.ToString(
                            CultureInfo.InvariantCulture),
                    ["protocolMin"] =
                        ProtocolRange.Minimum.ToString(
                            CultureInfo.InvariantCulture),
                    ["sessionId"] = SessionId,
                    ["token"] = Convert.ToBase64String(token)
                });
        }

        public static bool TryParse(
            byte[] payload,
            out CompanionSessionRegistration? registration,
            out string error)
        {
            registration = null;
            var decode = IpcPayloadCodec.TryDeserialize(payload);
            if (!decode.Success || decode.Fields == null)
            {
                error = decode.ErrorCode + ": " + decode.Error;
                return false;
            }

            var expected = new[]
            {
                "gameProcessId",
                "gameProcessStartTimeUtcTicks",
                "environmentManifestSha256",
                "runtimeAssemblySha256",
                "coreAssemblySha256",
                "automationMode",
                "debugMutationEnabled",
                "nativeCapabilitiesRequested",
                "pipeName",
                "protocolMax",
                "protocolMin",
                "sessionId",
                "token"
            };
            if (decode.Fields.Count != expected.Length
                || expected.Any(
                    field => !decode.Fields.ContainsKey(field)))
            {
                error = "Session registration has an invalid shape.";
                return false;
            }
            if (!IsLowerSha256(
                    decode.Fields[
                        "environmentManifestSha256"])
                || !IsLowerSha256(
                    decode.Fields["runtimeAssemblySha256"])
                || !IsLowerSha256(
                    decode.Fields["coreAssemblySha256"]))
            {
                error =
                    "Session registration contains invalid runtime hashes.";
                return false;
            }

            if (!int.TryParse(
                    decode.Fields["gameProcessId"],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var gameProcessId)
                || gameProcessId <= 0
                || !long.TryParse(
                    decode.Fields[
                        "gameProcessStartTimeUtcTicks"],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var gameProcessStartTimeUtcTicks)
                || gameProcessStartTimeUtcTicks <= 0
                || !int.TryParse(
                    decode.Fields["protocolMin"],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var protocolMinimum)
                || !int.TryParse(
                    decode.Fields["protocolMax"],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var protocolMaximum)
                || protocolMinimum <= 0
                || protocolMaximum < protocolMinimum)
            {
                error = "Session registration contains invalid numbers.";
                return false;
            }

            var nativeCapabilitiesText =
                decode.Fields[
                    "nativeCapabilitiesRequested"];
            bool nativeCapabilitiesRequested;
            if (string.Equals(
                    nativeCapabilitiesText,
                    "true",
                    StringComparison.Ordinal))
            {
                nativeCapabilitiesRequested = true;
            }
            else if (string.Equals(
                         nativeCapabilitiesText,
                         "false",
                         StringComparison.Ordinal))
            {
                nativeCapabilitiesRequested = false;
            }
            else
            {
                error =
                    "Session registration contains an invalid native capability request.";
                return false;
            }

            if (!AutomationModeCodec.TryParse(
                    decode.Fields["automationMode"],
                    out var automationMode))
            {
                error =
                    "Session registration contains an invalid automation mode.";
                return false;
            }

            var debugMutationText =
                decode.Fields["debugMutationEnabled"];
            bool debugMutationEnabled;
            if (string.Equals(
                    debugMutationText,
                    "true",
                    StringComparison.Ordinal))
            {
                debugMutationEnabled = true;
            }
            else if (string.Equals(
                         debugMutationText,
                         "false",
                         StringComparison.Ordinal))
            {
                debugMutationEnabled = false;
            }
            else
            {
                error =
                    "Session registration contains an invalid debug mutation setting.";
                return false;
            }

            byte[] token;
            try
            {
                token = Convert.FromBase64String(
                    decode.Fields["token"]);
            }
            catch (FormatException)
            {
                error = "Session registration token is invalid.";
                return false;
            }

            try
            {
                registration = new CompanionSessionRegistration(
                    decode.Fields["sessionId"],
                    gameProcessId,
                    gameProcessStartTimeUtcTicks,
                    decode.Fields[
                        "environmentManifestSha256"],
                    decode.Fields["runtimeAssemblySha256"],
                    decode.Fields["coreAssemblySha256"],
                    decode.Fields["pipeName"],
                    new ProtocolRange(
                        protocolMinimum,
                        protocolMaximum),
                    nativeCapabilitiesRequested,
                    token,
                    automationMode,
                    debugMutationEnabled);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static bool IsLowerSha256(string value)
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

    public static class CompanionInstanceNames
    {
        public static string GetCurrentUserIdentity()
        {
            var domain = Environment.UserDomainName;
            var user = Environment.UserName;
            if (string.IsNullOrWhiteSpace(user))
            {
                throw new InvalidOperationException(
                    "Current Windows user name is unavailable.");
            }

            return string.IsNullOrWhiteSpace(domain)
                ? user
                : domain + "\\" + user;
        }

        public static string CreateMutexName(string userIdentity)
        {
            return "Local\\HollowKnightTAS.Companion."
                   + CreateSuffix(userIdentity);
        }

        public static string CreateControlPipeName(string userIdentity)
        {
            return "HollowKnightTAS.Companion.Control."
                   + CreateSuffix(userIdentity);
        }

        private static string CreateSuffix(string userIdentity)
        {
            if (string.IsNullOrWhiteSpace(userIdentity)
                || userIdentity.Length > 1024)
            {
                throw new ArgumentException(
                    "Current-user identity is required.",
                    nameof(userIdentity));
            }

            return Sha256Utility.ComputeUtf8Hex(userIdentity)
                .Substring(0, 24);
        }
    }
}
