using System;
using System.Globalization;
using System.IO;
using System.Text;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Input;

namespace HollowKnightTAS.Core.Movie
{
    public sealed class MovieCanonicalWriter
    {
        public void Write(MovieDocument movie, TextWriter writer)
        {
            if (movie == null)
            {
                throw new ArgumentNullException(nameof(movie));
            }

            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            WriteHeader(movie.Header, writer);

            FrameRunCommand? pendingFrames = null;
            long pendingCount = 0;
            foreach (var command in movie.Commands)
            {
                if (command is FrameRunCommand frames)
                {
                    if (pendingFrames != null
                        && pendingFrames.HasSameInput(frames)
                        && pendingCount <= long.MaxValue - frames.FrameCount)
                    {
                        pendingCount += frames.FrameCount;
                        continue;
                    }

                    if (pendingFrames != null)
                    {
                        WriteFrames(pendingFrames, pendingCount, writer);
                    }

                    pendingFrames = frames;
                    pendingCount = frames.FrameCount;
                    continue;
                }

                if (pendingFrames != null)
                {
                    WriteFrames(pendingFrames, pendingCount, writer);
                    pendingFrames = null;
                    pendingCount = 0;
                }

                WriteNonFrameCommand(command, writer);
            }

            if (pendingFrames != null)
            {
                WriteFrames(pendingFrames, pendingCount, writer);
            }
        }

        public string WriteToString(MovieDocument movie)
        {
            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                Write(movie, writer);
                return writer.ToString();
            }
        }

        public string ComputeMovieId(MovieDocument movie)
        {
            return Sha256Utility.ComputeUtf8Hex(WriteToString(movie));
        }

        public byte[] WriteUtf8(MovieDocument movie)
        {
            return new UTF8Encoding(false, true).GetBytes(WriteToString(movie));
        }

        public static string EscapeQuoted(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (!MovieProtocolV1.HasValidUtf16(value))
            {
                throw new InvalidDataException(
                    "Quoted value contains an unpaired UTF-16 surrogate.");
            }

            var builder = new StringBuilder(value.Length + 2);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
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
                        if (character < ' ')
                        {
                            builder.Append("\\u");
                            builder.Append(
                                ((int)character).ToString(
                                    "x4",
                                    CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            return builder.ToString();
        }

        private static void WriteHeader(MovieHeader header, TextWriter writer)
        {
            writer.Write("hktas ");
            writer.Write(header.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
            writer.Write('\n');
            writer.Write("game ");
            writer.Write(header.GameVersion);
            writer.Write('\n');
            writer.Write("api ");
            writer.Write(header.ApiVersion);
            writer.Write('\n');
            writer.Write("manifest-sha256 ");
            writer.Write(header.ManifestSha256);
            writer.Write('\n');
            writer.Write("baseline ");
            writer.Write(header.BaselineId);
            writer.Write(' ');
            writer.Write(header.BaselineSha256);
            writer.Write('\n');
            writer.Write("tick-unit ");
            writer.Write(header.TickUnit);
            writer.Write('\n');
            writer.Write("---\n");
        }

        private static void WriteFrames(
            FrameRunCommand frames,
            long frameCount,
            TextWriter writer)
        {
            writer.Write("frames ");
            writer.Write(frameCount.ToString(CultureInfo.InvariantCulture));
            writer.Write(" hold=");
            WriteActions(frames.HeldActions, writer);
            if (frames.AxisX != 0 || frames.AxisY != 0)
            {
                writer.Write(" x=");
                writer.Write(frames.AxisX.ToString(CultureInfo.InvariantCulture));
                writer.Write(" y=");
                writer.Write(frames.AxisY.ToString(CultureInfo.InvariantCulture));
            }

            writer.Write('\n');
        }

        private static void WriteActions(TasAction actions, TextWriter writer)
        {
            if (actions == TasAction.None)
            {
                writer.Write('-');
                return;
            }

            if ((actions & ~TasAction.AllGameplay) != TasAction.None)
            {
                throw new InvalidDataException("Frame run contains unknown action bits.");
            }

            var first = true;
            foreach (var action in MovieProtocolV1.OrderedActions)
            {
                if ((actions & action) == TasAction.None)
                {
                    continue;
                }

                if (!first)
                {
                    writer.Write(',');
                }

                writer.Write(MovieProtocolV1.GetActionName(action));
                first = false;
            }
        }

        private static void WriteNonFrameCommand(
            MovieCommand command,
            TextWriter writer)
        {
            if (command is MarkerCommand marker)
            {
                writer.Write("marker \"");
                writer.Write(EscapeQuoted(marker.Text));
                writer.Write("\"\n");
                return;
            }

            if (command is CheckpointCommand checkpoint)
            {
                writer.Write("checkpoint ");
                writer.Write(checkpoint.Identifier);
                writer.Write('\n');
                return;
            }

            if (command is AssertCommand assertion)
            {
                writer.Write("assert ");
                writer.Write(assertion.SemanticPath);
                writer.Write(' ');
                writer.Write(assertion.Operator);
                writer.Write(' ');
                if (assertion.ValueIsQuoted)
                {
                    writer.Write('"');
                    writer.Write(EscapeQuoted(assertion.Value));
                    writer.Write('"');
                }
                else
                {
                    writer.Write(assertion.Value);
                }

                writer.Write('\n');
                return;
            }

            throw new InvalidDataException(
                "Movie contains an unsupported command type: "
                + (command == null ? "<null>" : command.GetType().FullName));
        }
    }
}
