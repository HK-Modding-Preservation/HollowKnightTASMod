using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HollowKnightTAS.Core.Input;

namespace HollowKnightTAS.Core.Movie
{
    public sealed class MovieParser
    {
        public const int MaximumSourceCharacters = 16 * 1024 * 1024;
        public const int MaximumLineCharacters = 64 * 1024;

        private static readonly string[] RequiredHeaderKeys =
        {
            "hktas",
            "game",
            "api",
            "manifest-sha256",
            "baseline",
            "tick-unit"
        };

        public MovieParseResult Parse(TextReader reader, string sourceName)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            sourceName = string.IsNullOrWhiteSpace(sourceName) ? "<movie>" : sourceName;
            var diagnostics = new List<MovieDiagnostic>();
            var source = ReadBounded(reader, sourceName, diagnostics);
            if (source == null)
            {
                return new MovieParseResult(null, diagnostics);
            }

            if (source.Length > 0 && source[0] == '\uFEFF')
            {
                diagnostics.Add(
                    Diagnostic(
                        MovieDiagnosticCodes.InvalidCharacter,
                        sourceName,
                        1,
                        1,
                        1,
                        "UTF-8 BOM is not allowed.",
                        "Save the movie as UTF-8 without BOM."));
                source = source.Substring(1);
            }

            var headers = new Dictionary<string, HeaderEntry>(StringComparer.Ordinal);
            var commands = new List<MovieCommand>();
            var inCommands = false;
            var separatorSeen = false;
            var lineNumber = 0;

            using (var stringReader = new StringReader(source))
            {
                string? line;
                while ((line = stringReader.ReadLine()) != null)
                {
                    lineNumber++;
                    if (line.Length > MaximumLineCharacters)
                    {
                        diagnostics.Add(
                            Diagnostic(
                                MovieDiagnosticCodes.LineTooLong,
                                sourceName,
                                lineNumber,
                                MaximumLineCharacters + 1,
                                line.Length - MaximumLineCharacters,
                                "Movie line exceeds the supported length.",
                                "Split or shorten the line."));
                        continue;
                    }

                    if (!TryLexLine(
                            line,
                            sourceName,
                            lineNumber,
                            diagnostics,
                            out var tokens))
                    {
                        continue;
                    }

                    if (tokens.Count == 0)
                    {
                        continue;
                    }

                    if (!inCommands
                        && tokens.Count == 1
                        && !tokens[0].Quoted
                        && string.Equals(tokens[0].Text, "---", StringComparison.Ordinal))
                    {
                        inCommands = true;
                        separatorSeen = true;
                        continue;
                    }

                    if (!inCommands)
                    {
                        ParseHeaderLine(tokens, sourceName, headers, diagnostics);
                    }
                    else
                    {
                        ParseCommandLine(tokens, sourceName, commands, diagnostics);
                    }
                }
            }

            if (!separatorSeen)
            {
                diagnostics.Add(
                    Diagnostic(
                        MovieDiagnosticCodes.MissingSeparator,
                        sourceName,
                        Math.Max(1, lineNumber),
                        1,
                        1,
                        "Header terminator '---' is missing.",
                        "Add a line containing only '---' after the six headers."));
            }

            foreach (var key in RequiredHeaderKeys)
            {
                if (!headers.ContainsKey(key))
                {
                    diagnostics.Add(
                        Diagnostic(
                            MovieDiagnosticCodes.MissingHeader,
                            sourceName,
                            1,
                            1,
                            1,
                            "Required header '" + key + "' is missing.",
                            "Add exactly one '" + key + "' header."));
                }
            }

            var header = BuildHeader(sourceName, headers, diagnostics);
            var document = header == null
                ? null
                : new MovieDocument(sourceName, header, commands);
            return new MovieParseResult(document, diagnostics);
        }

        private static string? ReadBounded(
            TextReader reader,
            string sourceName,
            ICollection<MovieDiagnostic> diagnostics)
        {
            var builder = new StringBuilder();
            var buffer = new char[4096];
            while (true)
            {
                var read = reader.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    return builder.ToString();
                }

                if (builder.Length > MaximumSourceCharacters - read)
                {
                    diagnostics.Add(
                        Diagnostic(
                            MovieDiagnosticCodes.SourceTooLarge,
                            sourceName,
                            1,
                            1,
                            1,
                            "Movie source exceeds "
                            + MaximumSourceCharacters.ToString(CultureInfo.InvariantCulture)
                            + " characters.",
                            "Reduce the movie source size before parsing."));
                    return null;
                }

                builder.Append(buffer, 0, read);
            }
        }

        private static bool TryLexLine(
            string line,
            string sourceName,
            int lineNumber,
            ICollection<MovieDiagnostic> diagnostics,
            out List<Token> tokens)
        {
            tokens = new List<Token>();
            var index = 0;
            while (index < line.Length)
            {
                while (index < line.Length && char.IsWhiteSpace(line[index]))
                {
                    index++;
                }

                if (index == line.Length || line[index] == '#')
                {
                    return true;
                }

                var tokenStart = index;
                if (line[index] == '"')
                {
                    index++;
                    var value = new StringBuilder();
                    var closed = false;
                    while (index < line.Length)
                    {
                        var character = line[index++];
                        if (character == '"')
                        {
                            closed = true;
                            break;
                        }

                        if (character == '\\')
                        {
                            if (index >= line.Length)
                            {
                                diagnostics.Add(
                                    Diagnostic(
                                        MovieDiagnosticCodes.InvalidEscape,
                                        sourceName,
                                        lineNumber,
                                        index,
                                        1,
                                        "String escape is incomplete.",
                                        "Use one of \\\", \\\\, \\n, \\r, \\t or \\uXXXX."));
                                return false;
                            }

                            var escaped = line[index++];
                            switch (escaped)
                            {
                                case '"':
                                    value.Append('"');
                                    break;
                                case '\\':
                                    value.Append('\\');
                                    break;
                                case 'n':
                                    value.Append('\n');
                                    break;
                                case 'r':
                                    value.Append('\r');
                                    break;
                                case 't':
                                    value.Append('\t');
                                    break;
                                case 'u':
                                    if (index > line.Length - 4
                                        || !TryParseHex4(line, index, out var unicode))
                                    {
                                        diagnostics.Add(
                                            Diagnostic(
                                                MovieDiagnosticCodes.InvalidEscape,
                                                sourceName,
                                                lineNumber,
                                                index,
                                                Math.Min(4, line.Length - index),
                                                "Unicode escape must contain four hexadecimal digits.",
                                                "Use the form \\uXXXX."));
                                        return false;
                                    }

                                    value.Append((char)unicode);
                                    index += 4;
                                    break;
                                default:
                                    diagnostics.Add(
                                        Diagnostic(
                                            MovieDiagnosticCodes.InvalidEscape,
                                            sourceName,
                                            lineNumber,
                                            index,
                                            1,
                                            "Unsupported string escape '\\" + escaped + "'.",
                                            "Use one of \\\", \\\\, \\n, \\r, \\t or \\uXXXX."));
                                    return false;
                            }

                            continue;
                        }

                        if (character < ' ')
                        {
                            diagnostics.Add(
                                Diagnostic(
                                    MovieDiagnosticCodes.InvalidCharacter,
                                    sourceName,
                                    lineNumber,
                                    index,
                                    1,
                                    "Literal control characters are not allowed in quoted text.",
                                    "Use a canonical escape sequence."));
                            return false;
                        }

                        value.Append(character);
                    }

                    if (!closed)
                    {
                        diagnostics.Add(
                            Diagnostic(
                                MovieDiagnosticCodes.UnterminatedString,
                                sourceName,
                                lineNumber,
                                tokenStart + 1,
                                Math.Max(1, line.Length - tokenStart),
                                "Quoted string is not terminated.",
                                "Add the closing double quote."));
                        return false;
                    }

                    if (index < line.Length
                        && !char.IsWhiteSpace(line[index])
                        && line[index] != '#')
                    {
                        diagnostics.Add(
                            Diagnostic(
                                MovieDiagnosticCodes.InvalidSyntax,
                                sourceName,
                                lineNumber,
                                index + 1,
                                1,
                                "Quoted tokens must be followed by whitespace or a comment.",
                                "Insert whitespace after the closing quote."));
                        return false;
                    }

                    if (!MovieProtocolV1.HasValidUtf16(value.ToString()))
                    {
                        diagnostics.Add(
                            Diagnostic(
                                MovieDiagnosticCodes.InvalidCharacter,
                                sourceName,
                                lineNumber,
                                tokenStart + 1,
                                Math.Max(1, index - tokenStart),
                                "Quoted text contains an unpaired UTF-16 surrogate.",
                                "Replace it with a valid Unicode scalar value."));
                        return false;
                    }

                    tokens.Add(
                        new Token(
                            value.ToString(),
                            true,
                            lineNumber,
                            tokenStart + 1,
                            index - tokenStart));
                    continue;
                }

                while (index < line.Length
                       && !char.IsWhiteSpace(line[index])
                       && line[index] != '#')
                {
                    var character = line[index];
                    if (character == '"'
                        || character == '\uFEFF'
                        || character < ' ')
                    {
                        diagnostics.Add(
                            Diagnostic(
                                MovieDiagnosticCodes.InvalidCharacter,
                                sourceName,
                                lineNumber,
                                index + 1,
                                1,
                                "Character is not valid in an unquoted token.",
                                "Quote text values and remove control/BOM characters."));
                        return false;
                    }

                    index++;
                }

                var text = line.Substring(tokenStart, index - tokenStart);
                if (!MovieProtocolV1.HasValidUtf16(text))
                {
                    diagnostics.Add(
                        Diagnostic(
                            MovieDiagnosticCodes.InvalidCharacter,
                            sourceName,
                            lineNumber,
                            tokenStart + 1,
                            Math.Max(1, text.Length),
                            "Token contains an unpaired UTF-16 surrogate.",
                            "Replace it with a valid Unicode scalar value."));
                    return false;
                }

                tokens.Add(
                    new Token(
                        text,
                        false,
                        lineNumber,
                        tokenStart + 1,
                        text.Length));
            }

            return true;
        }

        private static void ParseHeaderLine(
            IReadOnlyList<Token> tokens,
            string sourceName,
            IDictionary<string, HeaderEntry> headers,
            ICollection<MovieDiagnostic> diagnostics)
        {
            var keyToken = tokens[0];
            if (keyToken.Quoted)
            {
                diagnostics.Add(
                    Diagnostic(
                        MovieDiagnosticCodes.UnknownHeader,
                        sourceName,
                        keyToken,
                        "Header key cannot be quoted.",
                        "Use one of the six unquoted v1 header keys."));
                return;
            }

            var key = keyToken.Text;
            var expectedCount = ExpectedHeaderTokenCount(key);
            if (expectedCount == 0)
            {
                diagnostics.Add(
                    Diagnostic(
                        MovieDiagnosticCodes.UnknownHeader,
                        sourceName,
                        keyToken,
                        "Unknown header key '" + key + "'.",
                        "Remove it or use a defined HK-TAS Movie v1 header."));
                return;
            }

            if (headers.ContainsKey(key))
            {
                diagnostics.Add(
                    Diagnostic(
                        MovieDiagnosticCodes.DuplicateHeader,
                        sourceName,
                        keyToken,
                        "Header '" + key + "' appears more than once.",
                        "Keep exactly one occurrence."));
                return;
            }

            if (tokens.Count != expectedCount)
            {
                diagnostics.Add(
                    Diagnostic(
                        MovieDiagnosticCodes.InvalidSyntax,
                        sourceName,
                        keyToken,
                        "Header '" + key + "' has the wrong number of values.",
                        "Follow the HK-TAS Movie v1 header grammar."));
                return;
            }

            for (var index = 1; index < tokens.Count; index++)
            {
                if (tokens[index].Quoted)
                {
                    diagnostics.Add(
                        Diagnostic(
                            MovieDiagnosticCodes.InvalidSyntax,
                            sourceName,
                            tokens[index],
                            "Header values cannot be quoted.",
                            "Use the canonical unquoted header value."));
                    return;
                }
            }

            headers.Add(key, new HeaderEntry(keyToken, tokens));
        }

        private static MovieHeader? BuildHeader(
            string sourceName,
            IReadOnlyDictionary<string, HeaderEntry> headers,
            ICollection<MovieDiagnostic> diagnostics)
        {
            foreach (var key in RequiredHeaderKeys)
            {
                if (!headers.ContainsKey(key))
                {
                    return null;
                }
            }

            var versionToken = headers["hktas"].Tokens[1];
            if (!TryParseUnsignedInt32(versionToken.Text, out var version))
            {
                diagnostics.Add(
                    Diagnostic(
                        MovieDiagnosticCodes.InvalidHeaderValue,
                        sourceName,
                        versionToken,
                        "Protocol version must be an unsigned decimal integer.",
                        "Use 'hktas 1'."));
                version = 0;
            }

            var spans = new Dictionary<string, MovieSourceSpan>(StringComparer.Ordinal);
            foreach (var pair in headers)
            {
                var valueToken = pair.Value.Tokens[1];
                spans[pair.Key] = new MovieSourceSpan(
                    sourceName,
                    pair.Value.Line,
                    valueToken.Column,
                    valueToken.Length);
            }

            var baselineHashToken = headers["baseline"].Tokens[2];
            spans["baseline-sha256"] = new MovieSourceSpan(
                sourceName,
                baselineHashToken.Line,
                baselineHashToken.Column,
                baselineHashToken.Length);

            return new MovieHeader(
                version,
                headers["game"].Tokens[1].Text,
                headers["api"].Tokens[1].Text,
                headers["manifest-sha256"].Tokens[1].Text,
                headers["baseline"].Tokens[1].Text,
                headers["baseline"].Tokens[2].Text,
                headers["tick-unit"].Tokens[1].Text,
                spans);
        }

        private static void ParseCommandLine(
            IReadOnlyList<Token> tokens,
            string sourceName,
            ICollection<MovieCommand> commands,
            ICollection<MovieDiagnostic> diagnostics)
        {
            var command = tokens[0];
            if (command.Quoted)
            {
                diagnostics.Add(
                    Diagnostic(
                        MovieDiagnosticCodes.UnknownCommand,
                        sourceName,
                        command,
                        "Command name cannot be quoted.",
                        "Use an unquoted v1 command name."));
                return;
            }

            switch (command.Text)
            {
                case "frames":
                    ParseFrames(tokens, sourceName, commands, diagnostics);
                    break;
                case "marker":
                    ParseMarker(tokens, sourceName, commands, diagnostics);
                    break;
                case "checkpoint":
                    ParseCheckpoint(tokens, sourceName, commands, diagnostics);
                    break;
                case "assert":
                    ParseAssert(tokens, sourceName, commands, diagnostics);
                    break;
                default:
                    diagnostics.Add(
                        Diagnostic(
                            MovieDiagnosticCodes.UnknownCommand,
                            sourceName,
                            command,
                            "Unknown command '" + command.Text + "'.",
                            "Use frames, marker, checkpoint or assert."));
                    break;
            }
        }

        private static void ParseFrames(
            IReadOnlyList<Token> tokens,
            string sourceName,
            ICollection<MovieCommand> commands,
            ICollection<MovieDiagnostic> diagnostics)
        {
            if (tokens.Count < 3 || tokens[1].Quoted)
            {
                diagnostics.Add(
                    Diagnostic(
                        MovieDiagnosticCodes.InvalidCommand,
                        sourceName,
                        tokens[0],
                        "frames requires a positive count and hold= field.",
                        "Use 'frames <positive-int> hold=<actions|->'."));
                return;
            }

            if (!TryParseUnsignedInt64(tokens[1].Text, out var frameCount)
                || frameCount == 0
                || frameCount > long.MaxValue)
            {
                diagnostics.Add(
                    Diagnostic(
                        MovieDiagnosticCodes.InvalidCommand,
                        sourceName,
                        tokens[1],
                        "Frame count must be an integer in [1, 9223372036854775807].",
                        "Use a positive decimal frame count."));
                return;
            }

            var fields = new Dictionary<string, FieldToken>(StringComparer.Ordinal);
            for (var index = 2; index < tokens.Count; index++)
            {
                var token = tokens[index];
                if (token.Quoted)
                {
                    diagnostics.Add(
                        Diagnostic(
                            MovieDiagnosticCodes.InvalidCommand,
                            sourceName,
                            token,
                            "frames fields cannot be quoted.",
                            "Use key=value fields."));
                    return;
                }

                var equals = token.Text.IndexOf('=');
                if (equals <= 0
                    || equals == token.Text.Length - 1
                    || token.Text.IndexOf('=', equals + 1) >= 0)
                {
                    diagnostics.Add(
                        Diagnostic(
                            MovieDiagnosticCodes.InvalidCommand,
                            sourceName,
                            token,
                            "Invalid frames field '" + token.Text + "'.",
                            "Use exactly one '=' in each key=value field."));
                    return;
                }

                var name = token.Text.Substring(0, equals);
                var value = token.Text.Substring(equals + 1);
                if (name != "hold" && name != "x" && name != "y")
                {
                    diagnostics.Add(
                        Diagnostic(
                            MovieDiagnosticCodes.InvalidCommand,
                            sourceName,
                            token,
                            "Unknown frames field '" + name + "'.",
                            "Use only hold, x and y."));
                    return;
                }

                if (fields.ContainsKey(name))
                {
                    diagnostics.Add(
                        Diagnostic(
                            MovieDiagnosticCodes.DuplicateField,
                            sourceName,
                            token,
                            "frames field '" + name + "' appears more than once.",
                            "Keep exactly one occurrence."));
                    return;
                }

                fields.Add(name, new FieldToken(token, equals + 1, value));
            }

            if (!fields.TryGetValue("hold", out var hold))
            {
                diagnostics.Add(
                    Diagnostic(
                        MovieDiagnosticCodes.InvalidCommand,
                        sourceName,
                        tokens[0],
                        "frames is missing hold=.",
                        "Add hold=<actions|->."));
                return;
            }

            if (!TryParseActions(hold, sourceName, diagnostics, out var actions))
            {
                return;
            }

            var hasX = fields.TryGetValue("x", out var xField);
            var hasY = fields.TryGetValue("y", out var yField);
            if (hasX != hasY)
            {
                var field = hasX ? xField : yField;
                diagnostics.Add(
                    Diagnostic(
                        MovieDiagnosticCodes.InvalidCommand,
                        sourceName,
                        field.Token,
                        "Analog frames must specify both x= and y=.",
                        "Add the missing axis field or remove both."));
                return;
            }

            var axisX = 0;
            var axisY = 0;
            if (hasX)
            {
                if (!TryParseSignedInt32(xField.Value, out axisX))
                {
                    diagnostics.Add(
                        Diagnostic(
                            MovieDiagnosticCodes.InvalidCommand,
                            sourceName,
                            xField.Token,
                            "x must be a non-exponent decimal integer.",
                            "Use an integer in [-10000, 10000]."));
                    return;
                }

                if (!TryParseSignedInt32(yField.Value, out axisY))
                {
                    diagnostics.Add(
                        Diagnostic(
                            MovieDiagnosticCodes.InvalidCommand,
                            sourceName,
                            yField.Token,
                            "y must be a non-exponent decimal integer.",
                            "Use an integer in [-10000, 10000]."));
                    return;
                }
            }

            commands.Add(
                new FrameRunCommand(
                    (long)frameCount,
                    actions,
                    axisX,
                    axisY,
                    hasX,
                    Span(sourceName, tokens[0])));
        }

        private static bool TryParseActions(
            FieldToken hold,
            string sourceName,
            ICollection<MovieDiagnostic> diagnostics,
            out TasAction actions)
        {
            actions = TasAction.None;
            if (string.Equals(hold.Value, "-", StringComparison.Ordinal))
            {
                return true;
            }

            var start = 0;
            while (start <= hold.Value.Length)
            {
                var comma = hold.Value.IndexOf(',', start);
                var end = comma < 0 ? hold.Value.Length : comma;
                var name = hold.Value.Substring(start, end - start);
                if (!MovieProtocolV1.TryParseAction(name, out var action))
                {
                    diagnostics.Add(
                        new MovieDiagnostic(
                            MovieDiagnosticCodes.UnknownAction,
                            new MovieSourceSpan(
                                sourceName,
                                hold.Line,
                                hold.Token.Column + hold.ValueOffset + start,
                                Math.Max(1, name.Length)),
                            "Unknown action '" + name + "'.",
                            "Use a lowercase HK-TAS Movie v1 action name."));
                    return false;
                }

                if ((actions & action) != TasAction.None)
                {
                    diagnostics.Add(
                        new MovieDiagnostic(
                            MovieDiagnosticCodes.DuplicateField,
                            new MovieSourceSpan(
                                sourceName,
                                hold.Line,
                                hold.Token.Column + hold.ValueOffset + start,
                                Math.Max(1, name.Length)),
                            "Action '" + name + "' appears more than once.",
                            "List each held action once."));
                    return false;
                }

                actions |= action;
                if (comma < 0)
                {
                    break;
                }

                start = comma + 1;
            }

            return true;
        }

        private static void ParseMarker(
            IReadOnlyList<Token> tokens,
            string sourceName,
            ICollection<MovieCommand> commands,
            ICollection<MovieDiagnostic> diagnostics)
        {
            if (tokens.Count != 2 || !tokens[1].Quoted)
            {
                diagnostics.Add(
                    Diagnostic(
                        MovieDiagnosticCodes.InvalidCommand,
                        sourceName,
                        tokens[0],
                        "marker requires exactly one quoted string.",
                        "Use 'marker \"text\"'."));
                return;
            }

            commands.Add(new MarkerCommand(tokens[1].Text, Span(sourceName, tokens[0])));
        }

        private static void ParseCheckpoint(
            IReadOnlyList<Token> tokens,
            string sourceName,
            ICollection<MovieCommand> commands,
            ICollection<MovieDiagnostic> diagnostics)
        {
            if (tokens.Count != 2 || tokens[1].Quoted)
            {
                diagnostics.Add(
                    Diagnostic(
                        MovieDiagnosticCodes.InvalidCommand,
                        sourceName,
                        tokens[0],
                        "checkpoint requires exactly one unquoted identifier.",
                        "Use 'checkpoint <identifier>'."));
                return;
            }

            commands.Add(
                new CheckpointCommand(tokens[1].Text, Span(sourceName, tokens[0])));
        }

        private static void ParseAssert(
            IReadOnlyList<Token> tokens,
            string sourceName,
            ICollection<MovieCommand> commands,
            ICollection<MovieDiagnostic> diagnostics)
        {
            if (tokens.Count != 4 || tokens[1].Quoted || tokens[2].Quoted)
            {
                diagnostics.Add(
                    Diagnostic(
                        MovieDiagnosticCodes.InvalidCommand,
                        sourceName,
                        tokens[0],
                        "assert requires path, operator and one canonical value.",
                        "Use 'assert <path> <operator> <value>'."));
                return;
            }

            if (!tokens[3].Quoted
                && !MovieProtocolV1.IsCanonicalAssertLiteral(tokens[3].Text))
            {
                diagnostics.Add(
                    Diagnostic(
                        MovieDiagnosticCodes.InvalidAssertValue,
                        sourceName,
                        tokens[3],
                        "Unquoted assert value is not canonical.",
                        "Use true, false, null, a canonical decimal, or a quoted string."));
                return;
            }

            commands.Add(
                new AssertCommand(
                    tokens[1].Text,
                    tokens[2].Text,
                    tokens[3].Text,
                    tokens[3].Quoted,
                    Span(sourceName, tokens[0])));
        }

        private static int ExpectedHeaderTokenCount(string key)
        {
            switch (key)
            {
                case "hktas":
                case "game":
                case "api":
                case "manifest-sha256":
                case "tick-unit":
                    return 2;
                case "baseline":
                    return 3;
                default:
                    return 0;
            }
        }

        private static bool TryParseHex4(string line, int start, out int value)
        {
            value = 0;
            for (var offset = 0; offset < 4; offset++)
            {
                var character = line[start + offset];
                int digit;
                if (character >= '0' && character <= '9')
                {
                    digit = character - '0';
                }
                else if (character >= 'a' && character <= 'f')
                {
                    digit = character - 'a' + 10;
                }
                else if (character >= 'A' && character <= 'F')
                {
                    digit = character - 'A' + 10;
                }
                else
                {
                    return false;
                }

                value = value * 16 + digit;
            }

            return true;
        }

        private static bool TryParseUnsignedInt32(string value, out int result)
        {
            result = 0;
            return IsUnsignedDecimal(value)
                   && int.TryParse(
                       value,
                       NumberStyles.None,
                       CultureInfo.InvariantCulture,
                       out result);
        }

        private static bool TryParseUnsignedInt64(string value, out ulong result)
        {
            result = 0;
            return IsUnsignedDecimal(value)
                   && ulong.TryParse(
                       value,
                       NumberStyles.None,
                       CultureInfo.InvariantCulture,
                       out result);
        }

        private static bool TryParseSignedInt32(string value, out int result)
        {
            result = 0;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var start = value[0] == '-' ? 1 : 0;
            if (start == value.Length)
            {
                return false;
            }

            for (var index = start; index < value.Length; index++)
            {
                if (value[index] < '0' || value[index] > '9')
                {
                    return false;
                }
            }

            return int.TryParse(
                value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out result);
        }

        private static bool IsUnsignedDecimal(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] < '0' || value[index] > '9')
                {
                    return false;
                }
            }

            return true;
        }

        private static MovieDiagnostic Diagnostic(
            string code,
            string sourceName,
            Token token,
            string message,
            string action)
        {
            return new MovieDiagnostic(code, Span(sourceName, token), message, action);
        }

        private static MovieDiagnostic Diagnostic(
            string code,
            string sourceName,
            int line,
            int column,
            int length,
            string message,
            string action)
        {
            return new MovieDiagnostic(
                code,
                new MovieSourceSpan(sourceName, line, column, length),
                message,
                action);
        }

        private static MovieSourceSpan Span(string sourceName, Token token)
        {
            return new MovieSourceSpan(
                sourceName,
                token.Line,
                token.Column,
                token.Length);
        }

        private sealed class Token
        {
            public Token(
                string text,
                bool quoted,
                int line,
                int column,
                int length)
            {
                Text = text;
                Quoted = quoted;
                Line = line;
                Column = column;
                Length = Math.Max(1, length);
            }

            public string Text { get; }
            public bool Quoted { get; }
            public int Column { get; }
            public int Length { get; }
            public int Line { get; }
        }

        private sealed class HeaderEntry
        {
            public HeaderEntry(Token key, IReadOnlyList<Token> tokens)
            {
                Key = key;
                Tokens = tokens;
                Line = key.Line;
            }

            public Token Key { get; }
            public IReadOnlyList<Token> Tokens { get; }
            public int Line { get; }
        }

        private readonly struct FieldToken
        {
            public FieldToken(Token token, int valueOffset, string value)
            {
                Token = token;
                ValueOffset = valueOffset;
                Value = value;
            }

            public Token Token { get; }
            public int ValueOffset { get; }
            public string Value { get; }
            public int Line => Token.Line;
        }
    }
}
