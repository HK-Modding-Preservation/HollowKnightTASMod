using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HollowKnightTAS.Core.Automation;
using HollowKnightTAS.Core.State;

namespace HollowKnightTAS.Automation.Client
{
    public sealed class AutomationSemanticState
    {
        public AutomationSemanticState(
            AutomationStateEnvelope state,
            AutomationSemanticSnapshot snapshot)
        {
            State = state
                    ?? throw new ArgumentNullException(nameof(state));
            Snapshot = snapshot
                       ?? throw new ArgumentNullException(
                           nameof(snapshot));
            if (!string.Equals(
                    state.SemanticSnapshotSha256,
                    snapshot.Sha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Semantic snapshot hash does not match the state envelope.");
            }
        }

        public AutomationStateEnvelope State { get; }
        public AutomationSemanticSnapshot Snapshot { get; }

        public static AutomationSemanticState Parse(string stateJson)
        {
            var state = AutomationStateParser.Parse(stateJson);
            if (!state.Fields.TryGetValue(
                    "semanticSnapshotJson",
                    out var snapshotJson))
            {
                throw new InvalidDataException(
                    "Automation state omitted semanticSnapshotJson.");
            }

            return new AutomationSemanticState(
                state,
                AutomationSemanticSnapshot.Parse(snapshotJson));
        }

        public string ToFriendlyJson()
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(
                       stream,
                       new JsonWriterOptions
                       {
                           Indented = false,
                           SkipValidation = false
                       }))
            {
                writer.WriteStartObject();
                writer.WriteNumber(
                    "schemaVersion",
                    Core.Automation.AutomationProtocol.Version);
                writer.WritePropertyName("state");
                using (var stateDocument = JsonDocument.Parse(
                           AutomationStateCodec.Serialize(State)))
                {
                    stateDocument.RootElement.WriteTo(writer);
                }

                writer.WritePropertyName("semanticValues");
                writer.WriteStartObject();
                foreach (var pair in Snapshot.Values.OrderBy(
                             item => item.Key,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(pair.Key);
                    writer.WriteStartObject();
                    writer.WriteString("kind", pair.Value.Kind.ToString());
                    writer.WriteString(
                        "canonicalHex",
                        pair.Value.CanonicalHex);
                    writer.WritePropertyName("value");
                    pair.Value.WriteNativeJson(writer);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    public sealed class AutomationSemanticSnapshot
    {
        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(false, true);
        private readonly ReadOnlyDictionary<
            string,
            AutomationSemanticValue> values;

        private AutomationSemanticSnapshot(
            int schemaVersion,
            string sha256,
            IDictionary<string, AutomationSemanticValue> source)
        {
            SchemaVersion = schemaVersion;
            Sha256 = sha256;
            values = new ReadOnlyDictionary<
                string,
                AutomationSemanticValue>(
                new SortedDictionary<
                    string,
                    AutomationSemanticValue>(
                    source,
                    StringComparer.Ordinal));
        }

        public int SchemaVersion { get; }
        public string Sha256 { get; }
        public IReadOnlyDictionary<
            string,
            AutomationSemanticValue> Values => values;

        public AutomationSemanticValue this[string key] => values[key];

        public bool TryGetValue(
            string key,
            out AutomationSemanticValue value)
        {
            return values.TryGetValue(key, out value!);
        }

        public bool TryGetBoolean(string key, out bool value)
        {
            return TryGetTyped(
                key,
                SemanticValueKind.Boolean,
                candidate => (bool)candidate.NativeValue,
                out value);
        }

        public bool TryGetInt32(string key, out int value)
        {
            return TryGetTyped(
                key,
                SemanticValueKind.Int32,
                candidate => (int)candidate.NativeValue,
                out value);
        }

        public bool TryGetInt64(string key, out long value)
        {
            return TryGetTyped(
                key,
                SemanticValueKind.Int64,
                candidate => (long)candidate.NativeValue,
                out value);
        }

        public bool TryGetFloat32(string key, out float value)
        {
            return TryGetTyped(
                key,
                SemanticValueKind.Float32Bits,
                candidate => (float)candidate.NativeValue,
                out value);
        }

        public bool TryGetString(string key, out string value)
        {
            return TryGetTyped(
                key,
                SemanticValueKind.Utf8String,
                candidate => (string)candidate.NativeValue,
                out value!);
        }

        public static AutomationSemanticSnapshot Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException(
                    "Semantic snapshot JSON is empty.");
            }

            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 16
                });
            var root = document.RootElement;
            RequireClosedObject(
                root,
                "semantic snapshot",
                "schemaVersion",
                "sha256",
                "values");
            var schemaVersion =
                root.GetProperty("schemaVersion").GetInt32();
            if (schemaVersion != SemanticSnapshotSchemaV1.Version)
            {
                throw new InvalidDataException(
                    "Unsupported semantic snapshot schema version.");
            }

            var sha256 = RequireString(root, "sha256");
            if (!IsLowerSha256(sha256))
            {
                throw new InvalidDataException(
                    "Semantic snapshot SHA-256 is invalid.");
            }

            var array = root.GetProperty("values");
            if (array.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "Semantic snapshot values must be an array.");
            }

            var result = new Dictionary<
                string,
                AutomationSemanticValue>(
                StringComparer.Ordinal);
            string? previous = null;
            foreach (var item in array.EnumerateArray())
            {
                RequireClosedObject(
                    item,
                    "semantic value",
                    "key",
                    "kind",
                    "canonicalHex",
                    "displayValue");
                var key = RequireString(item, "key");
                if (key.Length == 0
                    || previous != null
                    && string.CompareOrdinal(previous, key) >= 0)
                {
                    throw new InvalidDataException(
                        "Semantic value keys must be non-empty, unique, and sorted.");
                }

                previous = key;
                var kindText = RequireString(item, "kind");
                if (!Enum.TryParse(
                        kindText,
                        false,
                        out SemanticValueKind kind)
                    || !Enum.IsDefined(typeof(SemanticValueKind), kind))
                {
                    throw new InvalidDataException(
                        "Semantic value kind is unknown.");
                }

                var canonicalHex =
                    RequireString(item, "canonicalHex");
                var displayValue =
                    RequireString(item, "displayValue");
                var value = AutomationSemanticValue.Parse(
                    kind,
                    canonicalHex,
                    displayValue,
                    StrictUtf8);
                result.Add(key, value);
            }

            return new AutomationSemanticSnapshot(
                schemaVersion,
                sha256,
                result);
        }

        private bool TryGetTyped<T>(
            string key,
            SemanticValueKind kind,
            Func<AutomationSemanticValue, T> convert,
            out T value)
        {
            if (values.TryGetValue(key, out var candidate)
                && candidate.Kind == kind)
            {
                value = convert(candidate);
                return true;
            }

            value = default!;
            return false;
        }

        private static void RequireClosedObject(
            JsonElement value,
            string label,
            params string[] names)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    label + " must be an object.");
            }

            var properties = value.EnumerateObject().ToArray();
            if (properties.Length != names.Length
                || properties.Select(item => item.Name)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != names.Length
                || names.Any(
                    name => !value.TryGetProperty(name, out _)))
            {
                throw new InvalidDataException(
                    label + " has an invalid closed shape.");
            }
        }

        private static string RequireString(
            JsonElement value,
            string name)
        {
            var property = value.GetProperty(name);
            if (property.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException(
                    name + " must be a string.");
            }

            return property.GetString()
                   ?? throw new InvalidDataException(
                       name + " cannot be null.");
        }

        private static bool IsLowerSha256(string value)
        {
            return value.Length == 64
                   && value.All(
                       character =>
                           character >= '0'
                           && character <= '9'
                           || character >= 'a'
                           && character <= 'f');
        }
    }

    public sealed class AutomationSemanticValue
    {
        private AutomationSemanticValue(
            SemanticValueKind kind,
            string canonicalHex,
            string displayValue,
            object nativeValue)
        {
            Kind = kind;
            CanonicalHex = canonicalHex;
            DisplayValue = displayValue;
            NativeValue = nativeValue;
        }

        public SemanticValueKind Kind { get; }
        public string CanonicalHex { get; }
        public string DisplayValue { get; }
        public object NativeValue { get; }

        internal static AutomationSemanticValue Parse(
            SemanticValueKind kind,
            string canonicalHex,
            string displayValue,
            UTF8Encoding strictUtf8)
        {
            var bytes = DecodeLowerHex(canonicalHex);
            object native;
            string expectedDisplay;
            var requireExactDisplay = true;
            switch (kind)
            {
                case SemanticValueKind.Boolean:
                    if (bytes.Length != 1 || bytes[0] > 1)
                    {
                        throw new InvalidDataException(
                            "Boolean semantic bytes must be 00 or 01.");
                    }

                    native = bytes[0] == 1;
                    expectedDisplay =
                        (bool)native ? "true" : "false";
                    break;
                case SemanticValueKind.Int32:
                    RequireLength(bytes, 4, kind);
                    native = BinaryPrimitives.ReadInt32BigEndian(bytes);
                    expectedDisplay = ((int)native).ToString(
                        CultureInfo.InvariantCulture);
                    break;
                case SemanticValueKind.Int64:
                    RequireLength(bytes, 8, kind);
                    native = BinaryPrimitives.ReadInt64BigEndian(bytes);
                    expectedDisplay = ((long)native).ToString(
                        CultureInfo.InvariantCulture);
                    break;
                case SemanticValueKind.Float32Bits:
                    RequireLength(bytes, 4, kind);
                    native = BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32BigEndian(bytes));
                    expectedDisplay = ((float)native).ToString(
                        "R",
                        CultureInfo.InvariantCulture);
                    // Unity's Mono and modern .NET can produce different
                    // round-trip strings for the same IEEE-754 bits. The
                    // canonical bytes are authoritative across runtimes.
                    requireExactDisplay = false;
                    break;
                case SemanticValueKind.Utf8String:
                    native = strictUtf8.GetString(bytes);
                    expectedDisplay = (string)native;
                    break;
                default:
                    throw new InvalidDataException(
                        "Semantic value kind is unknown.");
            }

            if (requireExactDisplay
                && !string.Equals(
                    displayValue,
                    expectedDisplay,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Semantic display value disagrees with canonical bytes.");
            }

            return new AutomationSemanticValue(
                kind,
                canonicalHex,
                displayValue,
                native);
        }

        internal void WriteNativeJson(Utf8JsonWriter writer)
        {
            switch (Kind)
            {
                case SemanticValueKind.Boolean:
                    writer.WriteBooleanValue((bool)NativeValue);
                    break;
                case SemanticValueKind.Int32:
                    writer.WriteNumberValue((int)NativeValue);
                    break;
                case SemanticValueKind.Int64:
                    writer.WriteNumberValue((long)NativeValue);
                    break;
                case SemanticValueKind.Float32Bits:
                    writer.WriteNumberValue((float)NativeValue);
                    break;
                case SemanticValueKind.Utf8String:
                    writer.WriteStringValue((string)NativeValue);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Semantic value kind is unknown.");
            }
        }

        private static byte[] DecodeLowerHex(string value)
        {
            if ((value.Length & 1) != 0
                || value.Any(
                    character =>
                        !(character >= '0' && character <= '9')
                        && !(character >= 'a' && character <= 'f')))
            {
                throw new InvalidDataException(
                    "canonicalHex must contain lowercase hex pairs.");
            }

            var bytes = new byte[value.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                bytes[index] = byte.Parse(
                    value.Substring(index * 2, 2),
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture);
            }

            return bytes;
        }

        private static void RequireLength(
            byte[] bytes,
            int expected,
            SemanticValueKind kind)
        {
            if (bytes.Length != expected)
            {
                throw new InvalidDataException(
                    kind + " canonical byte length is invalid.");
            }
        }
    }
}
