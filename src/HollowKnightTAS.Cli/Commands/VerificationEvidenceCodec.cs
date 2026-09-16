using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Verification;

namespace HollowKnightTAS.Cli.Commands
{
    internal static class VerificationEvidenceCodec
    {
        internal const int SchemaVersion = 1;

        public static RunEvidence Load(string runJsonPath)
        {
            if (string.IsNullOrWhiteSpace(runJsonPath))
            {
                throw new ArgumentException(
                    "A run evidence path is required.",
                    nameof(runJsonPath));
            }

            var path = Path.GetFullPath(runJsonPath);
            EvidenceCompleteness.RequireComplete(path);
            var bytes = File.ReadAllBytes(path);
            RejectUtf8Bom(bytes);
            using var document = JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64
                });
            var root = document.RootElement;
            RequireObject(root, "run evidence");
            RequireSchema(root);
            var projection = RequiredString(root, "semanticProjection");
            if (!string.Equals(
                    projection,
                    VerificationSnapshotNormalizer.ProjectionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Unsupported semanticProjection: " + projection);
            }

            var directory = Path.GetDirectoryName(path)
                            ?? throw new InvalidDataException(
                                "Run evidence path has no containing directory.");
            var milestonesElement = Required(root, "milestones");
            if (milestonesElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("milestones must be an array.");
            }

            var milestones = new List<MilestoneRecord>();
            foreach (var item in milestonesElement.EnumerateArray())
            {
                milestones.Add(ParseMilestone(item, directory));
            }

            var evidence = new RunEvidence(
                RequiredString(root, "sessionId"),
                RequiredString(root, "processInstanceId"),
                RequiredString(root, "manifestSha256"),
                RequiredString(root, "baselineSha256"),
                RequiredString(root, "movieId"),
                RequiredInt32(root, "snapshotSchemaVersion"),
                RequiredInt32(root, "ledgerSchemaVersion"),
                milestones);
            var declaredSignature = RequiredString(root, "runSignature");
            if (!string.Equals(
                    declaredSignature,
                    evidence.RunSignature,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "runSignature does not match canonical run evidence.");
            }

            return evidence;
        }

        private static MilestoneRecord ParseMilestone(
            JsonElement value,
            string runDirectory)
        {
            RequireObject(value, "milestone");
            var movieTick = RequiredInt64(value, "movieTick");
            var stamp = ParseTickStamp(Required(value, "tickStamp"));
            var input = ParseInput(Required(value, "input"));
            var snapshotPath = ResolveArtifact(
                runDirectory,
                RequiredString(value, "snapshotFile"));
            var ledgerElement = Required(value, "ledgerWindow");
            if (ledgerElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "ledgerWindow must be an array.");
            }

            var ledger = new List<VerificationLedgerEntry>();
            foreach (var entry in ledgerElement.EnumerateArray())
            {
                ledger.Add(ParseLedgerEntry(entry));
            }

            var milestone = new MilestoneRecord(
                RequiredString(value, "milestoneId"),
                movieTick,
                stamp,
                RequiredString(value, "sceneName"),
                ReadSnapshotArtifact(snapshotPath),
                input,
                ledger,
                RequiredString(value, "rngStateSha256"));
            if (!string.Equals(
                    RequiredString(value, "semanticSha256"),
                    milestone.SemanticSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Milestone semanticSha256 does not match snapshotFile.");
            }

            if (!string.Equals(
                    RequiredString(value, "ledgerWindowSha256"),
                    milestone.LedgerWindowSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Milestone ledgerWindowSha256 does not match ledgerWindow.");
            }

            return milestone;
        }

        private static VerificationLedgerEntry ParseLedgerEntry(
            JsonElement value)
        {
            RequireObject(value, "ledger entry");
            return new VerificationLedgerEntry(
                RequiredInt64(value, "movieTick"),
                ParseTickStamp(Required(value, "tickStamp")),
                ParseInput(Required(value, "input")),
                RequiredInt32(value, "fixedStepsSincePreviousVisual"),
                RequiredInt32(value, "timeMinusFixedTimeBits"),
                RequiredString(value, "sceneName"),
                RequiredString(value, "eventName"),
                RequiredString(value, "rngStateSha256"));
        }

        private static TickStamp ParseTickStamp(JsonElement value)
        {
            RequireObject(value, "tickStamp");
            var phaseText = RequiredString(value, "phase");
            if (!Enum.TryParse(
                    phaseText,
                    false,
                    out TickPhase phase)
                || !Enum.IsDefined(typeof(TickPhase), phase))
            {
                throw new InvalidDataException(
                    "tickStamp.phase is invalid: " + phaseText);
            }

            return new TickStamp(
                RequiredUInt64(value, "inputTick"),
                RequiredInt64(value, "visualTick"),
                RequiredInt64(value, "fixedTick"),
                RequiredInt32(value, "sceneEpoch"),
                phase);
        }

        private static InputSample ParseInput(JsonElement value)
        {
            RequireObject(value, "input");
            return new InputSample(
                RequiredUInt64(value, "inputTick"),
                ParseAction(value, "held"),
                ParseAction(value, "pressed"),
                ParseAction(value, "released"),
                checked((short)RequiredInt32(value, "axisX")),
                checked((short)RequiredInt32(value, "axisY")));
        }

        private static TasAction ParseAction(JsonElement root, string name)
        {
            var number = RequiredInt32(root, name);
            if (number < ushort.MinValue || number > ushort.MaxValue)
            {
                throw new InvalidDataException(name + " is outside UInt16.");
            }

            return (TasAction)(ushort)number;
        }

        private static string ResolveArtifact(
            string runDirectory,
            string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)
                || Path.IsPathRooted(relativePath))
            {
                throw new InvalidDataException(
                    "snapshotFile must be a relative path.");
            }

            var root = Path.GetFullPath(runDirectory);
            var candidate = Path.GetFullPath(
                Path.Combine(
                    root,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var prefix = root.EndsWith(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "snapshotFile escapes the run evidence directory.");
            }

            if (!File.Exists(candidate))
            {
                throw new FileNotFoundException(
                    "Milestone snapshot file was not found.",
                    candidate);
            }

            return candidate;
        }

        private static byte[] ReadSnapshotArtifact(string path)
        {
            EvidenceCompleteness.RequireComplete(path);
            if (!path.EndsWith(
                    ".hex",
                    StringComparison.OrdinalIgnoreCase))
            {
                return File.ReadAllBytes(path);
            }

            var encoded = File.ReadAllBytes(path);
            RejectUtf8Bom(encoded);
            var text = new UTF8Encoding(false, true).GetString(encoded);
            if (text.EndsWith("\r\n", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 2);
            }
            else if (text.EndsWith("\n", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 1);
            }

            if (text.Length == 0 || text.Length % 2 != 0)
            {
                throw new InvalidDataException(
                    "Hex snapshot must contain a non-empty even number of characters.");
            }

            var result = new byte[text.Length / 2];
            for (var index = 0; index < result.Length; index++)
            {
                var high = DecodeLowerHex(text[index * 2]);
                var low = DecodeLowerHex(text[(index * 2) + 1]);
                result[index] = (byte)((high << 4) | low);
            }

            return result;
        }

        private static int DecodeLowerHex(char value)
        {
            if (value >= '0' && value <= '9')
            {
                return value - '0';
            }

            if (value >= 'a' && value <= 'f')
            {
                return value - 'a' + 10;
            }

            throw new InvalidDataException(
                "Hex snapshot must use canonical lowercase hexadecimal.");
        }

        private static void RequireSchema(JsonElement root)
        {
            var version = RequiredInt32(root, "schemaVersion");
            if (version != SchemaVersion)
            {
                throw new InvalidDataException(
                    "Unsupported run evidence schema: "
                    + version.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void RequireObject(JsonElement value, string name)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(name + " must be an object.");
            }
        }

        private static JsonElement Required(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                throw new InvalidDataException(
                    "Missing required property: " + name);
            }

            return value;
        }

        private static string RequiredString(JsonElement root, string name)
        {
            var value = Required(root, name);
            if (value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException(name + " must be a string.");
            }

            return value.GetString()
                   ?? throw new InvalidDataException(name + " cannot be null.");
        }

        private static int RequiredInt32(JsonElement root, string name)
        {
            var value = Required(root, name);
            if (value.ValueKind != JsonValueKind.Number
                || !value.TryGetInt32(out var result))
            {
                throw new InvalidDataException(
                    name + " must be a 32-bit integer.");
            }

            return result;
        }

        private static long RequiredInt64(JsonElement root, string name)
        {
            var value = Required(root, name);
            if (value.ValueKind != JsonValueKind.Number
                || !value.TryGetInt64(out var result))
            {
                throw new InvalidDataException(
                    name + " must be a 64-bit integer.");
            }

            return result;
        }

        private static ulong RequiredUInt64(JsonElement root, string name)
        {
            var value = Required(root, name);
            if (value.ValueKind != JsonValueKind.Number
                || !value.TryGetUInt64(out var result))
            {
                throw new InvalidDataException(
                    name + " must be an unsigned 64-bit integer.");
            }

            return result;
        }

        private static void RejectUtf8Bom(byte[] bytes)
        {
            if (bytes.Length >= 3
                && bytes[0] == 0xEF
                && bytes[1] == 0xBB
                && bytes[2] == 0xBF)
            {
                throw new InvalidDataException(
                    "Run evidence JSON must not contain a UTF-8 BOM.");
            }
        }
    }
}
