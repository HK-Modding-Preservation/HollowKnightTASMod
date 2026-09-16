using System;
using System.Collections.Generic;
using System.Linq;
using HollowKnightTAS.Core.Ipc;

namespace HollowKnightTAS.Core.Automation
{
    public sealed class AutomationBootstrapDescriptor
    {
        private readonly byte[] token;

        public AutomationBootstrapDescriptor(
            string pipeName,
            string sessionId,
            string manifestSha256,
            AutomationMode mode,
            byte[] token)
        {
            if (!IpcIdentifier.IsValid(pipeName, 240)
                || !IpcIdentifier.IsValid(sessionId, 128)
                || !IsLowerSha256(manifestSha256)
                || mode == AutomationMode.Disabled
                || token == null
                || token.Length != 32)
            {
                throw new ArgumentException(
                    "Automation bootstrap binding is invalid.");
            }

            PipeName = pipeName;
            SessionId = sessionId;
            ManifestSha256 = manifestSha256;
            Mode = mode;
            this.token = (byte[])token.Clone();
        }

        public string PipeName { get; }
        public string SessionId { get; }
        public string ManifestSha256 { get; }
        public AutomationMode Mode { get; }
        public byte[] Token => (byte[])token.Clone();

        public byte[] ToBytes()
        {
            return IpcPayloadCodec.Serialize(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["manifestSha256"] = ManifestSha256,
                    ["mode"] = Mode.ToString(),
                    ["pipeName"] = PipeName,
                    ["product"] = AutomationProtocol.Product,
                    ["schemaVersion"] = "1",
                    ["sessionId"] = SessionId,
                    ["token"] = Convert.ToBase64String(token)
                });
        }

        public static bool TryParse(
            byte[] bytes,
            out AutomationBootstrapDescriptor? descriptor,
            out string error)
        {
            descriptor = null;
            var parsed = IpcPayloadCodec.TryDeserialize(bytes);
            var expected = new[]
            {
                "manifestSha256",
                "mode",
                "pipeName",
                "product",
                "schemaVersion",
                "sessionId",
                "token"
            };
            if (!parsed.Success
                || parsed.Fields == null
                || parsed.Fields.Count != expected.Length
                || expected.Any(
                    key => !parsed.Fields.ContainsKey(key))
                || parsed.Fields["product"] != AutomationProtocol.Product
                || parsed.Fields["schemaVersion"] != "1"
                || !AutomationModeCodec.TryParse(
                    parsed.Fields["mode"],
                    out var mode)
                || mode == AutomationMode.Disabled)
            {
                error = "Automation bootstrap shape is invalid.";
                return false;
            }

            try
            {
                descriptor = new AutomationBootstrapDescriptor(
                    parsed.Fields["pipeName"],
                    parsed.Fields["sessionId"],
                    parsed.Fields["manifestSha256"],
                    mode,
                    Convert.FromBase64String(
                        parsed.Fields["token"]));
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
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

    public static class AutomationHandshakeCodec
    {
        public static byte[] CreateHello(
            string clientId,
            string sessionId,
            string manifestSha256,
            byte[] nonce,
            byte[] token)
        {
            if (!IpcIdentifier.IsValid(clientId, 128)
                || nonce == null
                || nonce.Length != 32
                || token == null
                || token.Length != 32)
            {
                throw new ArgumentException(
                    "Automation hello values are invalid.");
            }

            return IpcPayloadCodec.Serialize(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["clientId"] = clientId,
                    ["clientNonce"] =
                        Convert.ToBase64String(nonce),
                    ["manifestSha256"] = manifestSha256,
                    ["product"] = AutomationProtocol.Product,
                    ["schemaVersion"] = "1",
                    ["sessionId"] = sessionId,
                    ["token"] = Convert.ToBase64String(token)
                });
        }
    }
}
