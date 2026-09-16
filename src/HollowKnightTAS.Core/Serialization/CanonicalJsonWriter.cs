using System;
using System.Globalization;
using System.Text;

namespace HollowKnightTAS.Core.Serialization
{
    public static class CanonicalJsonWriter
    {
        public static void AppendString(StringBuilder builder, string value)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            builder.Append('"');
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else if (char.IsHighSurrogate(character))
                        {
                            if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                            {
                                throw new ArgumentException("JSON strings cannot contain an unpaired high surrogate.", nameof(value));
                            }

                            builder.Append(character);
                            builder.Append(value[++index]);
                        }
                        else if (char.IsLowSurrogate(character))
                        {
                            throw new ArgumentException("JSON strings cannot contain an unpaired low surrogate.", nameof(value));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            builder.Append('"');
        }
    }
}

