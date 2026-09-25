using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HollowKnightTAS.Core.Cryptography;

namespace HollowKnightTAS.Core.Movie
{
    /// <summary>Bounded JSON Lines codec for the native-frame movie format.</summary>
    public sealed class MovieV2Codec
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public MovieV2ParseResult Parse(TextReader reader, string sourceName)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            sourceName = string.IsNullOrWhiteSpace(sourceName) ? "<movie>" : sourceName;
            var runs = new List<NativeFrameRun>();
            var diagnostics = new List<MovieDiagnostic>();
            MovieV2Header? header = null;
            long sourceBytes = 0;
            long expandedFrames = 0;
            var lineNumber = 0;
            var activeLine = 1;

            try
            {
                while (true)
                {
                    activeLine = lineNumber + 1;
                    if (!TryReadLine(reader, ref sourceBytes, out var line)) break;
                    lineNumber = activeLine;
                    if (lineNumber == 1 && line.Length > 0 && line[0] == '\uFEFF')
                        throw new FormatFault(MovieDiagnosticCodes.InvalidCharacter, 1, "UTF-8 BOM is not allowed.");
                    if (!MovieProtocolV1.HasValidUtf16(line))
                        throw new FormatFault(MovieDiagnosticCodes.InvalidCharacter, 1, "Invalid UTF-16 source character.");

                    var json = new JsonLineParser(line).Parse();
                    if (lineNumber == 1)
                    {
                        header = ReadHeader(json);
                    }
                    else
                    {
                        var run = ReadRun(json, sourceName, lineNumber);
                        if (run.RepeatCount > MovieProtocolV2.MaximumExpandedFrames - expandedFrames)
                            throw new FormatFault(MovieDiagnosticCodes.ExpandedTickLimit, 1, "Expanded native-frame limit exceeded.");
                        expandedFrames += run.RepeatCount;
                        runs.Add(run);
                    }
                }

                if (header == null)
                    throw new FormatFault(MovieDiagnosticCodes.MissingHeader, 1, "Movie v2 header is missing.");
                return new MovieV2ParseResult(new MovieV2Document(sourceName, header, runs), diagnostics);
            }
            catch (FormatFault fault)
            {
                diagnostics.Add(new MovieDiagnostic(fault.Code,
                    new MovieSourceSpan(sourceName, activeLine, fault.Column, 1),
                    fault.Message, "Correct the v2 movie source and retry."));
                return new MovieV2ParseResult(null, diagnostics);
            }
        }

        public string WriteCanonical(MovieV2Document movie)
        {
            if (movie == null) throw new ArgumentNullException(nameof(movie));
            ValidateHeader(movie.Header);
            var builder = new StringBuilder();
            builder.Append("{\"format\":\"hktas\",\"version\":2,\"tickUnit\":\"input-playerloop\",\"actionSchemaId\":");
            AppendString(builder, movie.Header.ActionSchemaId);
            builder.Append(",\"nativeProfileId\":");
            AppendString(builder, movie.Header.NativeProfileId);
            builder.Append(",\"mouseEnabled\":").Append(movie.Header.MouseEnabled ? "true" : "false");
            builder.Append(",\"gameVersion\":");
            AppendString(builder, movie.Header.GameVersion);
            builder.Append(",\"apiVersion\":");
            AppendString(builder, movie.Header.ApiVersion);
            builder.Append(",\"modVersion\":");
            AppendString(builder, movie.Header.ModVersion);
            builder.Append(",\"environmentSha256\":");
            AppendString(builder, movie.Header.EnvironmentSha256);
            builder.Append(",\"viewportWidth\":").Append(movie.Header.ViewportWidth.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"viewportHeight\":").Append(movie.Header.ViewportHeight.ToString(CultureInfo.InvariantCulture));
            builder.Append("}\n");
            EnsureSourceBudget(builder);

            long expandedFrames = 0;
            long pendingCount = 0;
            int pendingFps = 50;
            bool pendingAuthored = false;
            IReadOnlyList<GameInputSample>? pendingSamples = null;
            foreach (var run in movie.Runs)
            {
                ValidateRun(run);
                if (run.RepeatCount > MovieProtocolV2.MaximumExpandedFrames - expandedFrames)
                    throw new InvalidDataException("Expanded native-frame limit exceeded.");
                expandedFrames += run.RepeatCount;
                if (pendingSamples != null && pendingFps == run.FramesPerSecond && pendingAuthored == run.Authored && SameSamples(pendingSamples, run.Samples))
                {
                    pendingCount += run.RepeatCount;
                    continue;
                }
                if (pendingSamples != null)
                {
                    AppendRun(builder, pendingCount, pendingSamples, pendingFps, pendingAuthored);
                    EnsureSourceBudget(builder);
                }
                pendingCount = run.RepeatCount;
                pendingFps = run.FramesPerSecond;
                pendingAuthored = run.Authored;
                pendingSamples = run.Samples;
            }
            if (pendingSamples != null)
            {
                AppendRun(builder, pendingCount, pendingSamples, pendingFps, pendingAuthored);
                EnsureSourceBudget(builder);
            }
            var canonical = builder.ToString();
            if (StrictUtf8.GetByteCount(canonical) > MovieProtocolV2.MaximumSourceUtf8Bytes)
                throw new InvalidDataException("Canonical v2 movie exceeds the source byte limit.");
            return canonical;
        }

        public string ComputeMovieId(MovieV2Document movie)
        {
            return Sha256Utility.ComputeUtf8Hex(WriteCanonical(movie));
        }

        private static bool TryReadLine(TextReader reader, ref long sourceBytes, out string line)
        {
            var builder = new StringBuilder();
            var endedByNewline = false;
            while (true)
            {
                var read = reader.Read();
                if (read < 0) break;
                if (read == '\n')
                {
                    endedByNewline = true;
                    break;
                }
                if (builder.Length >= MovieProtocolV2.MaximumLineCharacters)
                    throw new FormatFault(MovieDiagnosticCodes.LineTooLong,
                        MovieProtocolV2.MaximumLineCharacters + 1, "Movie v2 line is too long.");
                builder.Append((char)read);
            }
            line = builder.ToString();
            if (line.Length == 0 && !endedByNewline) return false;
            try
            {
                sourceBytes += StrictUtf8.GetByteCount(line) + (endedByNewline ? 1 : 0);
            }
            catch (EncoderFallbackException)
            {
                throw new FormatFault(MovieDiagnosticCodes.InvalidCharacter, 1, "Invalid UTF-16 source character.");
            }
            if (sourceBytes > MovieProtocolV2.MaximumSourceUtf8Bytes)
                throw new FormatFault(MovieDiagnosticCodes.SourceTooLarge, 1, "Movie v2 source exceeds its byte limit.");
            return true;
        }

        private static MovieV2Header ReadHeader(JsonValue json)
        {
            var fields = ObjectFields(json, "format", "version", "tickUnit", "actionSchemaId", "nativeProfileId",
                "mouseEnabled", "gameVersion", "apiVersion", "modVersion", "environmentSha256", "viewportWidth", "viewportHeight");
            if (String(fields, "format") != MovieProtocolV2.Format)
                throw new FormatFault(MovieDiagnosticCodes.InvalidHeaderValue, json.Column, "Invalid movie format.");
            if (Integer(fields, "version", 0, int.MaxValue) != MovieProtocolV2.Version)
                throw new FormatFault(MovieDiagnosticCodes.UnsupportedVersion, json.Column, "Unsupported movie version.");
            if (String(fields, "tickUnit") != MovieProtocolV2.TickUnit)
                throw new FormatFault(MovieDiagnosticCodes.InvalidTickUnit, json.Column, "Invalid native-frame tick unit.");
            var header = new MovieV2Header(
                String(fields, "gameVersion"), String(fields, "apiVersion"), String(fields, "modVersion"),
                String(fields, "nativeProfileId"), String(fields, "actionSchemaId"),
                Boolean(fields, "mouseEnabled"), String(fields, "environmentSha256"),
                (int)Integer(fields, "viewportWidth", 0, 32768),
                (int)Integer(fields, "viewportHeight", 0, 32768));
            try { ValidateHeader(header); }
            catch (InvalidDataException error)
            {
                throw new FormatFault(MovieDiagnosticCodes.InvalidHeaderValue, json.Column, error.Message);
            }
            return header;
        }

        private static NativeFrameRun ReadRun(JsonValue json, string sourceName, int lineNumber)
        {
            var fields = ObjectFields(json, "repeatCount", "samples", "fps?", "authored?");
            var count = Integer(fields, "repeatCount", 1, MovieProtocolV2.MaximumExpandedFrames);
            var array = Field(fields, "samples");
            if (array.Kind != JsonKind.Array || array.Items == null)
                throw new FormatFault(MovieDiagnosticCodes.InvalidCommand, array.Column, "Samples must be an array.");
            if (array.Items.Count > MovieProtocolV2.MaximumSamplesPerFrame)
                throw new FormatFault(MovieDiagnosticCodes.InvalidCommand, array.Column, "Too many samples in one native frame.");
            var samples = new List<GameInputSample>(array.Items.Count);
            foreach (var item in array.Items) samples.Add(ReadSample(item));
            return new NativeFrameRun(count, samples, new MovieSourceSpan(sourceName, lineNumber, 1, json.Length),
                fields.ContainsKey("fps") ? (int)Integer(fields, "fps", 1, 1000) : 50,
                fields.ContainsKey("authored") && Boolean(fields, "authored"));
        }

        private static GameInputSample ReadSample(JsonValue json)
        {
            var fields = ObjectFields(json, "channel", "values", "pressedMask", "releasedMask", "mouse");
            var channelValue = String(fields, "channel");
            if (!MovieProtocolV2.TryParseChannel(channelValue, out var channel))
                throw new FormatFault(MovieDiagnosticCodes.UnknownAction, Field(fields, "channel").Column,
                    "Unknown or noncanonical input channel.");
            var valuesValue = Field(fields, "values");
            if (valuesValue.Kind != JsonKind.Array || valuesValue.Items == null)
                throw new FormatFault(MovieDiagnosticCodes.InvalidCommand, valuesValue.Column, "Values must be an array.");
            var expected = MovieProtocolV2.ExpectedValueCount(channel);
            if (valuesValue.Items.Count != expected)
                throw new FormatFault(MovieDiagnosticCodes.InvalidCommand, valuesValue.Column, "Input value count does not match its channel.");
            var values = new short[expected];
            for (var index = 0; index < expected; index++)
                values[index] = (short)Integer(valuesValue.Items[index], short.MinValue, short.MaxValue);
            var mouseValue = Field(fields, "mouse");
            MouseFrameState? mouse = null;
            if (MovieProtocolV2.IsMouseChannel(channel))
            {
                var mouseFields = ObjectFields(mouseValue, "xQ16", "yQ16", "deltaXQ15", "deltaYQ15", "buttons", "wheelQ15");
                mouse = new MouseFrameState(
                    (int)Integer(mouseFields, "xQ16", 0, ushort.MaxValue),
                    (int)Integer(mouseFields, "yQ16", 0, ushort.MaxValue),
                    (short)Integer(mouseFields, "deltaXQ15", short.MinValue, short.MaxValue),
                    (short)Integer(mouseFields, "deltaYQ15", short.MinValue, short.MaxValue),
                    (uint)Integer(mouseFields, "buttons", 0, uint.MaxValue),
                    (short)Integer(mouseFields, "wheelQ15", short.MinValue, short.MaxValue));
            }
            else if (mouseValue.Kind != JsonKind.Null)
            {
                throw new FormatFault(MovieDiagnosticCodes.InvalidCommand, mouseValue.Column, "Action sample mouse must be null.");
            }
            var pressedMask = (ulong)Integer(fields, "pressedMask", 0, long.MaxValue);
            var releasedMask = (ulong)Integer(fields, "releasedMask", 0, long.MaxValue);
            return new GameInputSample(channel, values, mouse, pressedMask, releasedMask);
        }

        private static Dictionary<string, JsonValue> ObjectFields(JsonValue json, params string[] expected)
        {
            if (json.Kind != JsonKind.Object || json.Fields == null)
                throw new FormatFault(MovieDiagnosticCodes.InvalidSyntax, json.Column, "Expected a JSON object.");
            foreach (var pair in json.Fields)
            {
                var known = false;
                foreach (var name in expected)
                    if (pair.Key == name.TrimEnd('?')) { known = true; break; }
                if (!known)
                    throw new FormatFault(MovieDiagnosticCodes.InvalidCommand, pair.Value.Column, "Unknown v2 field: " + pair.Key);
            }
            foreach (var name in expected)
                if (!name.EndsWith("?", StringComparison.Ordinal) && !json.Fields.ContainsKey(name))
                    throw new FormatFault(MovieDiagnosticCodes.MissingHeader, json.Column, "Missing v2 field: " + name);
            return json.Fields;
        }

        private static JsonValue Field(Dictionary<string, JsonValue> fields, string name) => fields[name];

        private static string String(Dictionary<string, JsonValue> fields, string name)
        {
            var value = Field(fields, name);
            if (value.Kind != JsonKind.String || value.Text == null)
                throw new FormatFault(MovieDiagnosticCodes.InvalidSyntax, value.Column, "Expected string field: " + name);
            return value.Text;
        }

        private static bool Boolean(Dictionary<string, JsonValue> fields, string name)
        {
            var value = Field(fields, name);
            if (value.Kind != JsonKind.Boolean)
                throw new FormatFault(MovieDiagnosticCodes.InvalidSyntax, value.Column, "Expected boolean field: " + name);
            return value.Flag;
        }

        private static long Integer(Dictionary<string, JsonValue> fields, string name, long min, long max)
            => Integer(Field(fields, name), min, max);

        private static long Integer(JsonValue value, long min, long max)
        {
            if (value.Kind != JsonKind.Number || value.Text == null || value.Text == "-0"
                || value.Text.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0
                || !long.TryParse(value.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result)
                || result < min || result > max)
                throw new FormatFault(MovieDiagnosticCodes.InvalidCommand, value.Column, "Expected bounded decimal integer.");
            return result;
        }

        private static void ValidateHeader(MovieV2Header header)
        {
            if (header == null) throw new InvalidDataException("Movie header is missing.");
            CheckHeaderString(header.GameVersion, "gameVersion");
            CheckHeaderString(header.ApiVersion, "apiVersion");
            CheckHeaderString(header.ModVersion, "modVersion");
            CheckHeaderString(header.NativeProfileId, "nativeProfileId");
            CheckHeaderString(header.ActionSchemaId, "actionSchemaId");
            if (header.EnvironmentSha256 != "none")
            {
                var hash = header.EnvironmentSha256;
                if (hash.Length != 64) throw new InvalidDataException("Invalid environment SHA-256.");
                foreach (var character in hash)
                    if (!(character >= '0' && character <= '9') && !(character >= 'a' && character <= 'f'))
                        throw new InvalidDataException("Invalid environment SHA-256.");
            }
            if (header.ViewportWidth < 0 || header.ViewportWidth > 32768
                || header.ViewportHeight < 0 || header.ViewportHeight > 32768
                || (header.ViewportWidth == 0) != (header.ViewportHeight == 0))
                throw new InvalidDataException("Invalid viewport dimensions.");
        }

        private static void CheckHeaderString(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || !MovieProtocolV1.HasValidUtf16(value))
                throw new InvalidDataException("Invalid " + name + ".");
            foreach (var character in value)
                if (char.IsControl(character)) throw new InvalidDataException("Invalid " + name + ".");
        }

        private static void ValidateRun(NativeFrameRun run)
        {
            if (run.FramesPerSecond < 1 || run.FramesPerSecond > 1000)
                throw new InvalidDataException("Frame rate must be between 1 and 1000 FPS.");
            if (run == null || run.RepeatCount < 1 || run.RepeatCount > MovieProtocolV2.MaximumExpandedFrames)
                throw new InvalidDataException("Invalid native-frame run count.");
            if (run.Samples.Count > MovieProtocolV2.MaximumSamplesPerFrame)
                throw new InvalidDataException("Too many samples in one native frame.");
            foreach (var sample in run.Samples)
            {
                var expected = MovieProtocolV2.ExpectedValueCount(sample.Channel);
                if (sample.Values.Count != expected) throw new InvalidDataException("Invalid channel value count.");
                var validMask = expected == 0 ? 0UL : (1UL << expected) - 1UL;
                if ((sample.PressedMask | sample.ReleasedMask) > long.MaxValue
                    || ((sample.PressedMask | sample.ReleasedMask) & ~validMask) != 0)
                    throw new InvalidDataException("Invalid action edge mask.");
                if (MovieProtocolV2.IsMouseChannel(sample.Channel))
                {
                    if (sample.Mouse == null || sample.Mouse.XQ16 < 0 || sample.Mouse.XQ16 > ushort.MaxValue
                        || sample.Mouse.YQ16 < 0 || sample.Mouse.YQ16 > ushort.MaxValue)
                        throw new InvalidDataException("Invalid mouse sample.");
                }
                else if (sample.Mouse != null) throw new InvalidDataException("Unexpected mouse sample.");
            }
        }

        private static void EnsureSourceBudget(StringBuilder builder)
        {
            // UTF-8 needs at least one byte per UTF-16 code unit for valid text.
            if (builder.Length > MovieProtocolV2.MaximumSourceUtf8Bytes)
                throw new InvalidDataException("Canonical v2 movie exceeds the source byte limit.");
        }

        private static void AppendRun(StringBuilder builder, long count, IReadOnlyList<GameInputSample> samples, int fps, bool authored)
        {
            var start = builder.Length;
            builder.Append("{\"repeatCount\":").Append(count.ToString(CultureInfo.InvariantCulture));
            if (fps != 50) builder.Append(",\"fps\":").Append(fps.ToString(CultureInfo.InvariantCulture));
            if (authored) builder.Append(",\"authored\":true");
            builder.Append(",\"samples\":[");
            for (var index = 0; index < samples.Count; index++)
            {
                if (index != 0) builder.Append(',');
                var sample = samples[index];
                builder.Append("{\"channel\":");
                AppendString(builder, MovieProtocolV2.GetChannelName(sample.Channel));
                builder.Append(",\"values\":[");
                for (var valueIndex = 0; valueIndex < sample.Values.Count; valueIndex++)
                {
                    if (valueIndex != 0) builder.Append(',');
                    builder.Append(sample.Values[valueIndex].ToString(CultureInfo.InvariantCulture));
                }
                builder.Append("],\"pressedMask\":").Append(sample.PressedMask.ToString(CultureInfo.InvariantCulture));
                builder.Append(",\"releasedMask\":").Append(sample.ReleasedMask.ToString(CultureInfo.InvariantCulture));
                builder.Append(",\"mouse\":");
                if (sample.Mouse == null) builder.Append("null");
                else
                {
                    var mouse = sample.Mouse;
                    builder.Append("{\"xQ16\":").Append(mouse.XQ16.ToString(CultureInfo.InvariantCulture));
                    builder.Append(",\"yQ16\":").Append(mouse.YQ16.ToString(CultureInfo.InvariantCulture));
                    builder.Append(",\"deltaXQ15\":").Append(mouse.DeltaXQ15.ToString(CultureInfo.InvariantCulture));
                    builder.Append(",\"deltaYQ15\":").Append(mouse.DeltaYQ15.ToString(CultureInfo.InvariantCulture));
                    builder.Append(",\"buttons\":").Append(mouse.Buttons.ToString(CultureInfo.InvariantCulture));
                    builder.Append(",\"wheelQ15\":").Append(mouse.WheelQ15.ToString(CultureInfo.InvariantCulture));
                    builder.Append('}');
                }
                builder.Append('}');
            }
            builder.Append("]}\n");
            if (builder.Length - start - 1 > MovieProtocolV2.MaximumLineCharacters)
                throw new InvalidDataException("Canonical v2 movie line exceeds its length limit.");
        }

        private static bool SameSamples(IReadOnlyList<GameInputSample> left, IReadOnlyList<GameInputSample> right)
        {
            if (left.Count != right.Count) return false;
            for (var index = 0; index < left.Count; index++)
            {
                var a = left[index]; var b = right[index];
                if (a.Channel != b.Channel || a.Values.Count != b.Values.Count
                    || a.PressedMask != b.PressedMask || a.ReleasedMask != b.ReleasedMask) return false;
                for (var value = 0; value < a.Values.Count; value++)
                    if (a.Values[value] != b.Values[value]) return false;
                if ((a.Mouse == null) != (b.Mouse == null)) return false;
                if (a.Mouse != null && b.Mouse != null
                    && (a.Mouse.XQ16 != b.Mouse.XQ16 || a.Mouse.YQ16 != b.Mouse.YQ16
                        || a.Mouse.DeltaXQ15 != b.Mouse.DeltaXQ15 || a.Mouse.DeltaYQ15 != b.Mouse.DeltaYQ15
                        || a.Mouse.Buttons != b.Mouse.Buttons || a.Mouse.WheelQ15 != b.Mouse.WheelQ15)) return false;
            }
            return true;
        }

        private static void AppendString(StringBuilder builder, string value)
        {
            if (!MovieProtocolV1.HasValidUtf16(value)) throw new InvalidDataException("Invalid UTF-16 movie string.");
            builder.Append('"');
            foreach (var character in value)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    default:
                        if (character < 0x20)
                            builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else builder.Append(character);
                        break;
                }
            }
            builder.Append('"');
        }

        private sealed class FormatFault : Exception
        {
            public FormatFault(string code, int column, string message) : base(message)
            { Code = code; Column = Math.Max(1, column); }
            public string Code { get; }
            public int Column { get; }
        }

        private enum JsonKind { Object, Array, String, Number, Boolean, Null }

        private sealed class JsonValue
        {
            public JsonValue(JsonKind kind, int column, int length, string? text = null,
                bool flag = false, Dictionary<string, JsonValue>? fields = null, List<JsonValue>? items = null)
            { Kind = kind; Column = column; Length = length; Text = text; Flag = flag; Fields = fields; Items = items; }
            public JsonKind Kind { get; }
            public int Column { get; }
            public int Length { get; }
            public string? Text { get; }
            public bool Flag { get; }
            public Dictionary<string, JsonValue>? Fields { get; }
            public List<JsonValue>? Items { get; }
        }

        private sealed class JsonLineParser
        {
            private readonly string line;
            private int index;
            private int nodes;
            public JsonLineParser(string line) { this.line = line; }

            public JsonValue Parse()
            {
                SkipSpace();
                var value = Value(0);
                SkipSpace();
                if (index != line.Length) Fault("Trailing JSON content.");
                return value;
            }

            private JsonValue Value(int depth)
            {
                if (depth > MovieProtocolV2.MaximumJsonDepth) Fault("JSON nesting depth exceeded.");
                if (++nodes > MovieProtocolV2.MaximumJsonNodesPerLine) Fault("JSON node limit exceeded.");
                SkipSpace();
                var start = index;
                if (index >= line.Length) Fault("JSON value is missing.");
                var character = line[index];
                if (character == '{') return Object(depth, start);
                if (character == '[') return Array(depth, start);
                if (character == '"')
                {
                    var value = StringToken();
                    return new JsonValue(JsonKind.String, start + 1, index - start, value);
                }
                if (character == 't' && Literal("true")) return new JsonValue(JsonKind.Boolean, start + 1, 4, flag: true);
                if (character == 'f' && Literal("false")) return new JsonValue(JsonKind.Boolean, start + 1, 5);
                if (character == 'n' && Literal("null")) return new JsonValue(JsonKind.Null, start + 1, 4);
                if (character == '-' || character >= '0' && character <= '9')
                    return new JsonValue(JsonKind.Number, start + 1, NumberToken().Length, line.Substring(start, index - start));
                Fault("Invalid JSON value.");
                throw new InvalidOperationException();
            }

            private JsonValue Object(int depth, int start)
            {
                index++;
                var fields = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
                SkipSpace();
                if (Take('}')) return new JsonValue(JsonKind.Object, start + 1, index - start, fields: fields);
                while (true)
                {
                    SkipSpace();
                    var keyColumn = index + 1;
                    if (index >= line.Length || line[index] != '"') Fault("JSON object key is missing.");
                    var key = StringToken();
                    SkipSpace();
                    if (!Take(':')) Fault("JSON object colon is missing.");
                    var value = Value(depth + 1);
                    if (fields.ContainsKey(key))
                        throw new FormatFault(MovieDiagnosticCodes.DuplicateField, keyColumn, "Duplicate JSON field: " + key);
                    fields.Add(key, value);
                    SkipSpace();
                    if (Take('}')) break;
                    if (!Take(',')) Fault("JSON object separator is missing.");
                }
                return new JsonValue(JsonKind.Object, start + 1, index - start, fields: fields);
            }

            private JsonValue Array(int depth, int start)
            {
                index++;
                var items = new List<JsonValue>();
                SkipSpace();
                if (Take(']')) return new JsonValue(JsonKind.Array, start + 1, index - start, items: items);
                while (true)
                {
                    items.Add(Value(depth + 1));
                    SkipSpace();
                    if (Take(']')) break;
                    if (!Take(',')) Fault("JSON array separator is missing.");
                }
                return new JsonValue(JsonKind.Array, start + 1, index - start, items: items);
            }

            private string StringToken()
            {
                index++;
                var value = new StringBuilder();
                while (index < line.Length)
                {
                    var character = line[index++];
                    if (character == '"')
                    {
                        var result = value.ToString();
                        if (!MovieProtocolV1.HasValidUtf16(result))
                            throw new FormatFault(MovieDiagnosticCodes.InvalidCharacter, index, "Invalid Unicode surrogate in JSON string.");
                        return result;
                    }
                    if (character < 0x20) Fault("Unescaped JSON control character.");
                    if (character != '\\') { value.Append(character); continue; }
                    if (index >= line.Length) Fault("Incomplete JSON escape.");
                    character = line[index++];
                    switch (character)
                    {
                        case '"': value.Append('"'); break;
                        case '\\': value.Append('\\'); break;
                        case '/': value.Append('/'); break;
                        case 'b': value.Append('\b'); break;
                        case 'f': value.Append('\f'); break;
                        case 'n': value.Append('\n'); break;
                        case 'r': value.Append('\r'); break;
                        case 't': value.Append('\t'); break;
                        case 'u':
                            if (index > line.Length - 4) Fault("Incomplete Unicode escape.");
                            var code = 0;
                            for (var digit = 0; digit < 4; digit++)
                            {
                                var hex = line[index++];
                                var parsed = hex >= '0' && hex <= '9' ? hex - '0'
                                    : hex >= 'a' && hex <= 'f' ? hex - 'a' + 10
                                    : hex >= 'A' && hex <= 'F' ? hex - 'A' + 10 : -1;
                                if (parsed < 0) Fault("Invalid Unicode escape.");
                                code = code * 16 + parsed;
                            }
                            value.Append((char)code);
                            break;
                        default: Fault("Invalid JSON escape."); break;
                    }
                }
                Fault("Unterminated JSON string.");
                throw new InvalidOperationException();
            }

            private string NumberToken()
            {
                var start = index;
                Take('-');
                if (Take('0')) { }
                else
                {
                    if (index >= line.Length || line[index] < '1' || line[index] > '9') Fault("Invalid JSON number.");
                    while (Digit()) index++;
                }
                if (Take('.'))
                {
                    if (!Digit()) Fault("Invalid JSON fraction.");
                    while (Digit()) index++;
                }
                if (Take('e') || Take('E'))
                {
                    if (!Take('+')) Take('-');
                    if (!Digit()) Fault("Invalid JSON exponent.");
                    while (Digit()) index++;
                }
                return line.Substring(start, index - start);
            }

            private bool Digit() => index < line.Length && line[index] >= '0' && line[index] <= '9';
            private bool Take(char character)
            {
                if (index >= line.Length || line[index] != character) return false;
                index++;
                return true;
            }
            private bool Literal(string literal)
            {
                if (index > line.Length - literal.Length
                    || string.CompareOrdinal(line, index, literal, 0, literal.Length) != 0) return false;
                index += literal.Length;
                return true;
            }
            private void SkipSpace()
            {
                while (index < line.Length && (line[index] == ' ' || line[index] == '\t' || line[index] == '\r')) index++;
            }
            private void Fault(string message)
                => throw new FormatFault(MovieDiagnosticCodes.InvalidSyntax, index + 1, message);
        }
    }
}
