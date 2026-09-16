using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HollowKnightTAS.Core.Automation;

namespace HollowKnightTAS.Automation.Client
{
    public static class AutomationStateParser
    {
        private static readonly string[] ExpectedProperties =
        {
            "activeCapabilities",
            "ageMilliseconds",
            "capturedAtUtc",
            "fields",
            "manifestSha256",
            "movieTick",
            "runtimeMode",
            "schemaVersion",
            "semanticSnapshotSha256",
            "sessionId",
            "tickPhase"
        };

        public static AutomationStateEnvelope Parse(string json)
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling =
                        JsonCommentHandling.Disallow,
                    MaxDepth = 16
                });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().Count()
                   != ExpectedProperties.Length
                || ExpectedProperties.Any(
                    name => !root.TryGetProperty(name, out _))
                || root.GetProperty("schemaVersion").GetInt32()
                   != AutomationProtocol.Version)
            {
                throw new InvalidDataException(
                    "Automation state has an invalid closed shape.");
            }

            var fieldsElement = root.GetProperty("fields");
            if (fieldsElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "Automation state fields must be an object.");
            }

            var fields = new Dictionary<string, string>(
                StringComparer.Ordinal);
            foreach (var property in fieldsElement
                         .EnumerateObject())
            {
                if (property.Value.ValueKind
                    != JsonValueKind.String
                    || !fields.TryAdd(
                        property.Name,
                        property.Value.GetString()!))
                {
                    throw new InvalidDataException(
                        "Automation state fields are invalid.");
                }
            }

            var capabilitiesElement =
                root.GetProperty("activeCapabilities");
            if (capabilitiesElement.ValueKind
                != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "Active capabilities must be an array.");
            }

            var capabilities = capabilitiesElement
                .EnumerateArray()
                .Select(
                    item =>
                        item.ValueKind == JsonValueKind.String
                            ? item.GetString()!
                            : throw new InvalidDataException(
                                "Active capability IDs must be strings."))
                .ToArray();
            var result = new AutomationStateEnvelope(
                root.GetProperty("sessionId").GetString()!,
                root.GetProperty("manifestSha256").GetString()!,
                root.GetProperty("runtimeMode").GetString()!,
                root.GetProperty("movieTick").GetInt64(),
                root.GetProperty("tickPhase").GetString()!,
                root.GetProperty("capturedAtUtc")
                    .GetDateTimeOffset(),
                root.GetProperty("ageMilliseconds").GetInt64(),
                root.GetProperty("semanticSnapshotSha256")
                    .GetString()!,
                fields,
                capabilities);
            if (!string.Equals(
                    AutomationStateCodec.Serialize(result),
                    json,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Automation state is not canonical.");
            }

            return result;
        }
    }
}
