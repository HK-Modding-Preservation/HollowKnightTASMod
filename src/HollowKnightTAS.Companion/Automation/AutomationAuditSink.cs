using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Ipc;

namespace HollowKnightTAS.Companion.Automation
{
    public sealed class AutomationAuditSink
    {
        private readonly object sync = new object();
        private readonly string path;
        private readonly byte[] salt;

        public AutomationAuditSink(
            string root,
            string sessionId,
            byte[] sessionSalt)
        {
            var directory = System.IO.Path.Combine(
                System.IO.Path.GetFullPath(root),
                sessionId,
                "audit");
            Directory.CreateDirectory(directory);
            path = System.IO.Path.Combine(
                directory,
                "automation-v1.jsonl");
            salt = (byte[])sessionSalt.Clone();
        }

        public string Path => path;

        public void Write(
            string correlationId,
            string clientId,
            string requestId,
            string idempotencyKey,
            string sessionId,
            string manifestSha256,
            string commandOrResource,
            string scope,
            string leaseId,
            long requestedAtTick,
            long acceptedAtTick,
            long completedAtTick,
            string resultCode,
            string sideEffectSummary,
            string responseSha256)
        {
            var bytes = IpcPayloadCodec.Serialize(
                new SortedDictionary<string, string>(
                    StringComparer.Ordinal)
                {
                    ["acceptedAtTick"] =
                        acceptedAtTick.ToString(
                            CultureInfo.InvariantCulture),
                    ["clientIdHash"] = Hash(clientId),
                    ["commandOrResource"] = commandOrResource,
                    ["completedAtTick"] =
                        completedAtTick.ToString(
                            CultureInfo.InvariantCulture),
                    ["correlationId"] = correlationId,
                    ["idempotencyKeyHash"] =
                        Hash(idempotencyKey),
                    ["leaseIdHash"] = Hash(leaseId),
                    ["manifestSha256"] = manifestSha256,
                    ["requestId"] = requestId,
                    ["requestedAtTick"] =
                        requestedAtTick.ToString(
                            CultureInfo.InvariantCulture),
                    ["responseSha256"] = responseSha256,
                    ["resultCode"] = resultCode,
                    ["schemaVersion"] = "1",
                    ["scope"] = scope,
                    ["sessionId"] = sessionId,
                    ["sideEffectSummary"] =
                        Sanitize(sideEffectSummary),
                    ["timestampUtc"] =
                        DateTimeOffset.UtcNow.ToString(
                            "O",
                            CultureInfo.InvariantCulture),
                    ["transport"] = "current-user-named-pipe"
                });
            lock (sync)
            {
                using (var stream = new FileStream(
                           path,
                           FileMode.Append,
                           FileAccess.Write,
                           FileShare.Read))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.WriteByte((byte)'\n');
                    stream.Flush(true);
                }
            }
        }

        private string Hash(string value)
        {
            using (var hmac = new HMACSHA256(salt))
            {
                return BitConverter.ToString(
                        hmac.ComputeHash(
                            new UTF8Encoding(false, true).GetBytes(
                                value ?? string.Empty)))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static string Sanitize(string value)
        {
            var result = (value ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\\', '/');
            return result.Substring(0, Math.Min(256, result.Length));
        }
    }
}
