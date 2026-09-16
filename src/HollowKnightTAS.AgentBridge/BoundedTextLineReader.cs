using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HollowKnightTAS.AgentBridge
{
    internal sealed class BoundedTextLineReader
    {
        private const int BufferCharacters = 4096;
        private readonly TextReader reader;
        private readonly char[] buffer =
            new char[BufferCharacters];
        private int bufferOffset;
        private int bufferCount;

        public BoundedTextLineReader(TextReader reader)
        {
            this.reader = reader
                          ?? throw new ArgumentNullException(
                              nameof(reader));
        }

        public async ValueTask<BoundedTextLine> ReadAsync(
            int maximumCharacters,
            CancellationToken cancellationToken)
        {
            if (maximumCharacters < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumCharacters));
            }

            var builder = new StringBuilder(
                Math.Min(maximumCharacters, BufferCharacters));
            var oversized = false;
            var sawCharacter = false;
            while (true)
            {
                if (bufferOffset >= bufferCount)
                {
                    bufferCount = await reader.ReadAsync(
                        buffer.AsMemory(),
                        cancellationToken);
                    bufferOffset = 0;
                    if (bufferCount == 0)
                    {
                        if (!sawCharacter)
                        {
                            return BoundedTextLine.EndOfStream();
                        }

                        return Complete(builder, oversized);
                    }
                }

                var character = buffer[bufferOffset++];
                if (character == '\n')
                {
                    return Complete(builder, oversized);
                }

                sawCharacter = true;
                if (oversized)
                {
                    continue;
                }

                if (builder.Length >= maximumCharacters)
                {
                    oversized = true;
                    builder.Clear();
                    continue;
                }

                builder.Append(character);
            }
        }

        private static BoundedTextLine Complete(
            StringBuilder builder,
            bool oversized)
        {
            if (oversized)
            {
                return BoundedTextLine.Oversized();
            }

            if (builder.Length > 0
                && builder[builder.Length - 1] == '\r')
            {
                builder.Length--;
            }

            return BoundedTextLine.Value(builder.ToString());
        }
    }

    internal readonly struct BoundedTextLine
    {
        private BoundedTextLine(
            string? text,
            bool isEndOfStream,
            bool isOversized)
        {
            Text = text;
            IsEndOfStream = isEndOfStream;
            IsOversized = isOversized;
        }

        public string? Text { get; }
        public bool IsEndOfStream { get; }
        public bool IsOversized { get; }

        public static BoundedTextLine Value(string value)
        {
            return new BoundedTextLine(value, false, false);
        }

        public static BoundedTextLine EndOfStream()
        {
            return new BoundedTextLine(null, true, false);
        }

        public static BoundedTextLine Oversized()
        {
            return new BoundedTextLine(null, false, true);
        }
    }
}
