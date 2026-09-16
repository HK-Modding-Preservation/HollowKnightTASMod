using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HollowKnightTAS.Cli.Commands;
using HollowKnightTAS.Cli.Commands.Automation;
using HollowKnightTAS.Core.Manifest;

namespace HollowKnightTAS.Cli
{
    internal static class Program
    {
        private const int UsageError = 2;
        private const int ValidationError = 3;

        private static int Main(string[] args)
        {
            return Run(args, Console.Out, Console.Error);
        }

        internal static int Run(string[] args, TextWriter standardOutput, TextWriter standardError)
        {
            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            if (standardOutput == null)
            {
                throw new ArgumentNullException(nameof(standardOutput));
            }

            if (standardError == null)
            {
                throw new ArgumentNullException(nameof(standardError));
            }

            try
            {
                if (args.Length == 3
                    && string.Equals(args[0], "manifest", StringComparison.Ordinal)
                    && string.Equals(args[1], "validate", StringComparison.Ordinal))
                {
                    return RunManifestValidate(
                        args[2],
                        standardOutput);
                }

                if (args.Length >= 2
                    && string.Equals(args[0], "movie", StringComparison.Ordinal))
                {
                    var commandArgs = args.Skip(2).ToArray();
                    switch (args[1])
                    {
                        case "validate":
                            return MovieValidateCommand.Run(
                                commandArgs,
                                standardOutput,
                                standardError);
                        case "format":
                            return MovieFormatCommand.Run(
                                commandArgs,
                                standardOutput,
                                standardError);
                        case "inspect":
                            return MovieInspectCommand.Run(
                                commandArgs,
                                standardOutput,
                                standardError);
                    }
                }

                if (args.Length >= 2
                    && string.Equals(
                        args[0],
                        "automation",
                        StringComparison.Ordinal))
                {
                    var commandArgs = args.Skip(2).ToArray();
                    switch (args[1])
                    {
                        case "status":
                            return AutomationCommands.Status(
                                commandArgs,
                                standardOutput);
                        case "startup":
                            return AutomationCommands.Startup(commandArgs, standardOutput);
                        case "state":
                            return AutomationCommands.State(
                                commandArgs,
                                standardOutput);
                        case "call":
                            return AutomationCommands.Call(
                                commandArgs,
                                standardOutput);
                        case "watch":
                            return AutomationCommands.Watch(
                                commandArgs,
                                standardOutput);
                    }
                }

                if (args.Length >= 2
                    && string.Equals(
                        args[0],
                        "verification",
                        StringComparison.Ordinal))
                {
                    var commandArgs = args.Skip(2).ToArray();
                    switch (args[1])
                    {
                        case "validate":
                            return VerifyRunCommand.Validate(
                                commandArgs,
                                standardOutput,
                                standardError);
                        case "compare":
                            return CompareRunsCommand.Run(
                                commandArgs,
                                standardOutput,
                                standardError);
                        case "campaign":
                            return VerifyRunCommand.Campaign(
                                commandArgs,
                                standardOutput,
                                standardError);
                    }
                }

                WriteUsage(standardError);
                return UsageError;
            }
            catch (MovieContentException)
            {
                return ValidationError;
            }
            catch (Exception exception) when (
                exception is IOException
                || exception is InvalidDataException
                || exception is UnauthorizedAccessException
                || exception is JsonException
                || exception is ArgumentException
                || exception is InvalidOperationException
                || exception is FormatException)
            {
                standardError.WriteLine(
                    "INVALID "
                    + exception.Message.Replace('\r', ' ').Replace('\n', ' '));
                return ValidationError;
            }
        }

        private static int RunManifestValidate(
            string manifestPath,
            TextWriter standardOutput)
        {
            var path = Path.GetFullPath(manifestPath);
            var bytes = File.ReadAllBytes(path);
            RejectUtf8Bom(bytes);

            var utf8 = new UTF8Encoding(false, true);
            var json = utf8.GetString(bytes);
            var manifest = Parse(json);
            var canonical = ManifestCanonicalizer.Serialize(manifest);
            if (!bytes.SequenceEqual(canonical))
            {
                throw new InvalidDataException(
                    "Manifest bytes are valid JSON but not canonical UTF-8.");
            }

            var computedHash = ManifestCanonicalizer.ComputeSha256(manifest);
            ValidateSiblingHash(path, computedHash);
            standardOutput.WriteLine("VALID " + computedHash);
            return 0;
        }

        private static void WriteUsage(TextWriter standardError)
        {
            standardError.WriteLine(
                "Usage: HollowKnightTAS.Cli manifest validate <manifest-path>");
            standardError.WriteLine(MovieValidateCommand.Usage);
            standardError.WriteLine(MovieFormatCommand.Usage);
            standardError.WriteLine(MovieInspectCommand.Usage);
            standardError.WriteLine(VerifyRunCommand.ValidateUsage);
            standardError.WriteLine(CompareRunsCommand.Usage);
            standardError.WriteLine(VerifyRunCommand.CampaignUsage);
            standardError.WriteLine(AutomationCommands.StatusUsage);
            standardError.WriteLine(AutomationCommands.StateUsage);
            standardError.WriteLine(AutomationCommands.CallUsage);
            standardError.WriteLine(AutomationCommands.WatchUsage);
        }

        private static EnvironmentManifest Parse(string json)
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64
                });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Manifest root must be an object.");
            }

            var schemaVersion = RequiredInt32(root, "schemaVersion");
            return new EnvironmentManifest(
                schemaVersion: schemaVersion,
                gameVersion: RequiredString(root, "gameVersion"),
                moddingApiVersion: RequiredString(root, "moddingApiVersion"),
                assemblySha256: RequiredStringDictionary(root, "assemblySha256"),
                loadedMods: RequiredStringDictionary(root, "loadedMods"),
                tasSettingsSha256: RequiredString(root, "tasSettingsSha256"),
                baselineId: RequiredString(root, "baselineId"),
                baselineSha256: RequiredString(root, "baselineSha256"),
                operatingSystem: RequiredString(root, "operatingSystem"),
                architecture: RequiredString(root, "architecture"),
                locale: RequiredString(root, "locale"),
                gameLanguage: RequiredString(root, "gameLanguage"),
                graphicsQualityLevel: RequiredInt32(root, "graphicsQualityLevel"),
                screenWidth: RequiredInt32(root, "screenWidth"),
                screenHeight: RequiredInt32(root, "screenHeight"),
                fullScreenMode: RequiredString(root, "fullScreenMode"),
                targetFrameRate: RequiredInt32(root, "targetFrameRate"),
                audioEnabled: RequiredBoolean(root, "audioEnabled"),
                verificationModeRequested: RequiredBoolean(root, "verificationModeRequested"),
                verificationModeAllowed: RequiredBoolean(root, "verificationModeAllowed"),
                unexpectedMods: RequiredStringArray(root, "unexpectedMods"),
                rngCodecId: schemaVersion >= 2
                    ? RequiredString(root, "rngCodecId")
                    : "not-captured",
                rngCoverage: schemaVersion >= 2
                    ? RequiredString(root, "rngCoverage")
                    : "none");
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
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            {
                throw new InvalidDataException(name + " must be a 32-bit integer.");
            }

            return result;
        }

        private static bool RequiredBoolean(JsonElement root, string name)
        {
            var value = Required(root, name);
            if (value.ValueKind != JsonValueKind.True
                && value.ValueKind != JsonValueKind.False)
            {
                throw new InvalidDataException(name + " must be a boolean.");
            }

            return value.GetBoolean();
        }

        private static IReadOnlyDictionary<string, string> RequiredStringDictionary(
            JsonElement root,
            string name)
        {
            var value = Required(root, name);
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(name + " must be an object.");
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException(name + " values must be strings.");
                }

                if (!result.TryAdd(
                        property.Name,
                        property.Value.GetString()
                        ?? throw new InvalidDataException(name + " values cannot be null.")))
                {
                    throw new InvalidDataException(name + " contains a duplicate key.");
                }
            }

            return result;
        }

        private static IReadOnlyList<string> RequiredStringArray(
            JsonElement root,
            string name)
        {
            var value = Required(root, name);
            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(name + " must be an array.");
            }

            var result = new List<string>();
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException(name + " items must be strings.");
                }

                result.Add(
                    item.GetString()
                    ?? throw new InvalidDataException(name + " items cannot be null."));
            }

            return result;
        }

        private static JsonElement Required(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var result))
            {
                throw new InvalidDataException("Missing required property: " + name);
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
                throw new InvalidDataException("Canonical manifest must not contain a UTF-8 BOM.");
            }
        }

        private static void ValidateSiblingHash(string manifestPath, string computedHash)
        {
            var directory = Path.GetDirectoryName(manifestPath)
                            ?? throw new InvalidDataException(
                                "Manifest path has no containing directory.");
            var sibling = Path.Combine(directory, "manifest.sha256");
            if (!File.Exists(sibling))
            {
                return;
            }

            var expected = File.ReadAllText(sibling, new UTF8Encoding(false, true)).Trim();
            if (!string.Equals(expected, computedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "manifest.sha256 does not match the canonical manifest.");
            }
        }
    }
}
