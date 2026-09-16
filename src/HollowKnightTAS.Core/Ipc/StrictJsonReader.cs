using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HollowKnightTAS.Core.Ipc
{
    internal sealed class StrictJsonReader
    {
        private readonly string value;
        private int position;

        public StrictJsonReader(string value)
        {
            this.value = value
                         ?? throw new ArgumentNullException(nameof(value));
        }

        public bool IsAtEnd
        {
            get
            {
                SkipWhitespace();
                return position == value.Length;
            }
        }

        public bool TryReadObjectStart(out string error)
        {
            return TryReadCharacter('{', out error);
        }

        public bool TryReadObjectEnd(out string error)
        {
            return TryReadCharacter('}', out error);
        }

        public bool TryReadArrayStart(out string error)
        {
            return TryReadCharacter('[', out error);
        }

        public bool TryReadArrayEnd(out string error)
        {
            return TryReadCharacter(']', out error);
        }

        public bool TryReadComma(out string error)
        {
            return TryReadCharacter(',', out error);
        }

        public bool TryReadColon(out string error)
        {
            return TryReadCharacter(':', out error);
        }

        public bool PeekObjectEnd()
        {
            SkipWhitespace();
            return position < value.Length && value[position] == '}';
        }

        public bool PeekArrayEnd()
        {
            SkipWhitespace();
            return position < value.Length && value[position] == ']';
        }

        public bool TryReadPropertyName(
            string expected,
            out string error)
        {
            if (!TryReadString(out var actual, out error))
            {
                return false;
            }

            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                error = "Expected property "
                        + expected
                        + " at character "
                        + position.ToString(CultureInfo.InvariantCulture)
                        + ".";
                return false;
            }

            return TryReadColon(out error);
        }

        public bool TryReadInt64(
            out long result,
            out string error)
        {
            SkipWhitespace();
            var start = position;
            if (position < value.Length && value[position] == '-')
            {
                position++;
            }

            var digitStart = position;
            while (position < value.Length
                   && value[position] >= '0'
                   && value[position] <= '9')
            {
                position++;
            }

            if (digitStart == position)
            {
                result = 0;
                error = "Expected JSON integer at character "
                        + start.ToString(CultureInfo.InvariantCulture)
                        + ".";
                return false;
            }

            if (position - digitStart > 1 && value[digitStart] == '0')
            {
                result = 0;
                error = "JSON integer has a leading zero.";
                return false;
            }

            var text = value.Substring(start, position - start);
            if (!long.TryParse(
                    text,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out result))
            {
                error = "JSON integer is outside Int64 range.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public bool TryReadString(
            out string result,
            out string error)
        {
            SkipWhitespace();
            if (position >= value.Length || value[position] != '"')
            {
                result = string.Empty;
                error = "Expected JSON string at character "
                        + position.ToString(CultureInfo.InvariantCulture)
                        + ".";
                return false;
            }

            position++;
            var builder = new StringBuilder();
            while (position < value.Length)
            {
                var character = value[position++];
                if (character == '"')
                {
                    result = builder.ToString();
                    error = string.Empty;
                    return true;
                }

                if (character < 0x20)
                {
                    result = string.Empty;
                    error = "JSON string contains an unescaped control character.";
                    return false;
                }

                if (character == '\\')
                {
                    if (!TryReadEscape(builder, out error))
                    {
                        result = string.Empty;
                        return false;
                    }

                    continue;
                }

                if (char.IsHighSurrogate(character))
                {
                    if (position >= value.Length
                        || !char.IsLowSurrogate(value[position]))
                    {
                        result = string.Empty;
                        error = "JSON string contains an unpaired high surrogate.";
                        return false;
                    }

                    builder.Append(character);
                    builder.Append(value[position++]);
                    continue;
                }

                if (char.IsLowSurrogate(character))
                {
                    result = string.Empty;
                    error = "JSON string contains an unpaired low surrogate.";
                    return false;
                }

                builder.Append(character);
            }

            result = string.Empty;
            error = "JSON string is unterminated.";
            return false;
        }

        public bool TryReadStringMap(
            int maximumFieldCount,
            int maximumFieldValueCharacters,
            out SortedDictionary<string, string>? fields,
            out string error)
        {
            fields = new SortedDictionary<string, string>(
                StringComparer.Ordinal);
            if (!TryReadObjectStart(out error))
            {
                fields = null;
                return false;
            }

            if (PeekObjectEnd())
            {
                TryReadObjectEnd(out _);
                return true;
            }

            while (true)
            {
                if (!TryReadString(out var name, out error)
                    || !TryReadColon(out error)
                    || !TryReadString(out var fieldValue, out error))
                {
                    fields = null;
                    return false;
                }

                if (!IpcIdentifier.IsValid(name, 64))
                {
                    fields = null;
                    error = "Payload field name is invalid.";
                    return false;
                }

                if (fieldValue.Length > maximumFieldValueCharacters)
                {
                    fields = null;
                    error = "Payload field value is too large.";
                    return false;
                }

                if (fields.ContainsKey(name))
                {
                    fields = null;
                    error = "Payload contains a duplicate field.";
                    return false;
                }

                fields.Add(name, fieldValue);
                if (fields.Count > maximumFieldCount)
                {
                    fields = null;
                    error = "Payload contains too many fields.";
                    return false;
                }

                if (PeekObjectEnd())
                {
                    TryReadObjectEnd(out _);
                    return true;
                }

                if (!TryReadComma(out error))
                {
                    fields = null;
                    return false;
                }
            }
        }

        private bool TryReadEscape(
            StringBuilder builder,
            out string error)
        {
            if (position >= value.Length)
            {
                error = "JSON escape is unterminated.";
                return false;
            }

            var escaped = value[position++];
            switch (escaped)
            {
                case '"':
                case '\\':
                case '/':
                    builder.Append(escaped);
                    error = string.Empty;
                    return true;
                case 'b':
                    builder.Append('\b');
                    error = string.Empty;
                    return true;
                case 'f':
                    builder.Append('\f');
                    error = string.Empty;
                    return true;
                case 'n':
                    builder.Append('\n');
                    error = string.Empty;
                    return true;
                case 'r':
                    builder.Append('\r');
                    error = string.Empty;
                    return true;
                case 't':
                    builder.Append('\t');
                    error = string.Empty;
                    return true;
                case 'u':
                    if (!TryReadHexCharacter(out var character, out error))
                    {
                        return false;
                    }

                    if (char.IsHighSurrogate(character))
                    {
                        if (position + 1 >= value.Length
                            || value[position] != '\\'
                            || value[position + 1] != 'u')
                        {
                            error = "Escaped high surrogate is not paired.";
                            return false;
                        }

                        position += 2;
                        if (!TryReadHexCharacter(
                                out var low,
                                out error)
                            || !char.IsLowSurrogate(low))
                        {
                            error = "Escaped high surrogate has no low surrogate.";
                            return false;
                        }

                        builder.Append(character);
                        builder.Append(low);
                        return true;
                    }

                    if (char.IsLowSurrogate(character))
                    {
                        error = "Escaped low surrogate is unpaired.";
                        return false;
                    }

                    builder.Append(character);
                    return true;
                default:
                    error = "JSON escape is invalid.";
                    return false;
            }
        }

        private bool TryReadHexCharacter(
            out char result,
            out string error)
        {
            if (position + 4 > value.Length)
            {
                result = default;
                error = "Unicode escape is truncated.";
                return false;
            }

            var code = 0;
            for (var index = 0; index < 4; index++)
            {
                var character = value[position++];
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
                    result = default;
                    error = "Unicode escape contains a non-hex character.";
                    return false;
                }

                code = (code << 4) | digit;
            }

            result = (char)code;
            error = string.Empty;
            return true;
        }

        private bool TryReadCharacter(
            char expected,
            out string error)
        {
            SkipWhitespace();
            if (position >= value.Length || value[position] != expected)
            {
                error = "Expected '"
                        + expected
                        + "' at character "
                        + position.ToString(CultureInfo.InvariantCulture)
                        + ".";
                return false;
            }

            position++;
            error = string.Empty;
            return true;
        }

        private void SkipWhitespace()
        {
            while (position < value.Length)
            {
                var character = value[position];
                if (character != ' '
                    && character != '\t'
                    && character != '\r'
                    && character != '\n')
                {
                    return;
                }

                position++;
            }
        }
    }
}
