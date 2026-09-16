using System;
using System.Globalization;
using System.IO;
using System.Text;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Runtime.ReplaySave
{
    public sealed class ForcedSceneTransitionSpec
    {
        public ForcedSceneTransitionSpec(
            string sceneName,
            string entryGateName,
            float entryDelay,
            bool alwaysUnloadUnusedAssets,
            bool preventCameraFadeOut,
            bool waitForSceneTransitionCameraFade,
            GameManager.SceneLoadVisualizations visualization)
        {
            if (string.IsNullOrWhiteSpace(sceneName)
                || sceneName.Length > 256
                || string.IsNullOrWhiteSpace(entryGateName)
                || entryGateName.Length > 256)
            {
                throw new ArgumentException(
                    "Scene and entry-gate names are required and bounded.");
            }

            if (float.IsNaN(entryDelay)
                || float.IsInfinity(entryDelay)
                || entryDelay < 0f
                || entryDelay > 60f)
            {
                throw new ArgumentOutOfRangeException(nameof(entryDelay));
            }

            SceneName = sceneName;
            EntryGateName = entryGateName;
            EntryDelay = entryDelay;
            AlwaysUnloadUnusedAssets = alwaysUnloadUnusedAssets;
            PreventCameraFadeOut = preventCameraFadeOut;
            WaitForSceneTransitionCameraFade =
                waitForSceneTransitionCameraFade;
            Visualization = visualization;
        }

        public string SceneName { get; }
        public string EntryGateName { get; }
        public float EntryDelay { get; }
        public bool AlwaysUnloadUnusedAssets { get; }
        public bool PreventCameraFadeOut { get; }
        public bool WaitForSceneTransitionCameraFade { get; }
        public GameManager.SceneLoadVisualizations Visualization { get; }

        public GameManager.SceneLoadInfo ToSceneLoadInfo()
        {
            return new GameManager.SceneLoadInfo
            {
                SceneName = SceneName,
                EntryGateName = EntryGateName,
                EntryDelay = EntryDelay,
                AlwaysUnloadUnusedAssets = AlwaysUnloadUnusedAssets,
                PreventCameraFadeOut = PreventCameraFadeOut,
                WaitForSceneTransitionCameraFade =
                    WaitForSceneTransitionCameraFade,
                Visualization = Visualization
            };
        }
    }

    public static class ForcedSceneTransitionCommand
    {
        public const string Prefix = "hktas.force-scene.";
        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(false, true);

        public static CheckpointCommand CreateMovieCommand(
            ForcedSceneTransitionSpec spec,
            MovieSourceSpan span)
        {
            return new CheckpointCommand(Encode(spec), span);
        }

        public static string Encode(ForcedSceneTransitionSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            using (var stream = new MemoryStream())
            {
                stream.WriteByte(1);
                WriteString(stream, spec.SceneName);
                WriteString(stream, spec.EntryGateName);
                WriteInt32(
                    stream,
                    SingleBits.FromSingle(spec.EntryDelay));
                var flags = 0;
                if (spec.AlwaysUnloadUnusedAssets)
                {
                    flags |= 1;
                }

                if (spec.PreventCameraFadeOut)
                {
                    flags |= 2;
                }

                if (spec.WaitForSceneTransitionCameraFade)
                {
                    flags |= 4;
                }

                stream.WriteByte((byte)flags);
                WriteInt32(stream, (int)spec.Visualization);
                var token = Convert
                    .ToBase64String(stream.ToArray())
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');
                var identifier = Prefix + token;
                if (!MovieProtocolV1.IsIdentifier(identifier))
                {
                    throw new InvalidDataException(
                        "Forced scene transition command is not a Movie v1 identifier.");
                }

                return identifier;
            }
        }

        public static bool TryDecode(
            string identifier,
            out ForcedSceneTransitionSpec? spec,
            out string error)
        {
            spec = null;
            error = string.Empty;
            if (identifier == null
                || !identifier.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                var token = identifier.Substring(Prefix.Length)
                    .Replace('-', '+')
                    .Replace('_', '/');
                switch (token.Length % 4)
                {
                    case 2:
                        token += "==";
                        break;
                    case 3:
                        token += "=";
                        break;
                    case 1:
                        throw new InvalidDataException(
                            "Forced scene transition base64 length is invalid.");
                }

                var bytes = Convert.FromBase64String(token);
                var cursor = 0;
                if (ReadByte(bytes, ref cursor) != 1)
                {
                    throw new InvalidDataException(
                        "Unsupported forced scene transition schema.");
                }

                var scene = ReadString(bytes, ref cursor);
                var gate = ReadString(bytes, ref cursor);
                var delay = SingleBits.ToSingle(
                    ReadInt32(bytes, ref cursor));
                var flags = ReadByte(bytes, ref cursor);
                if ((flags & ~7) != 0)
                {
                    throw new InvalidDataException(
                        "Forced scene transition flags are invalid.");
                }

                var visualization = ReadInt32(bytes, ref cursor);
                if (cursor != bytes.Length
                    || !Enum.IsDefined(
                        typeof(GameManager.SceneLoadVisualizations),
                        visualization))
                {
                    throw new InvalidDataException(
                        "Forced scene transition payload is invalid.");
                }

                spec = new ForcedSceneTransitionSpec(
                    scene,
                    gate,
                    delay,
                    (flags & 1) != 0,
                    (flags & 2) != 0,
                    (flags & 4) != 0,
                    (GameManager.SceneLoadVisualizations)visualization);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private static void WriteString(Stream stream, string value)
        {
            var bytes = StrictUtf8.GetBytes(value);
            if (bytes.Length == 0 || bytes.Length > 1024)
            {
                throw new InvalidDataException(
                    "Forced scene transition string length is invalid.");
            }

            WriteInt32(stream, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string ReadString(byte[] bytes, ref int cursor)
        {
            var count = ReadInt32(bytes, ref cursor);
            if (count <= 0 || count > 1024 || bytes.Length - cursor < count)
            {
                throw new InvalidDataException(
                    "Forced scene transition string length is invalid.");
            }

            var value = StrictUtf8.GetString(bytes, cursor, count);
            cursor += count;
            return value;
        }

        private static void WriteInt32(Stream stream, int value)
        {
            unchecked
            {
                stream.WriteByte((byte)value);
                stream.WriteByte((byte)((uint)value >> 8));
                stream.WriteByte((byte)((uint)value >> 16));
                stream.WriteByte((byte)((uint)value >> 24));
            }
        }

        private static int ReadInt32(byte[] bytes, ref int cursor)
        {
            if (bytes.Length - cursor < 4)
            {
                throw new InvalidDataException(
                    "Forced scene transition payload is truncated.");
            }

            unchecked
            {
                var value = bytes[cursor]
                            | bytes[cursor + 1] << 8
                            | bytes[cursor + 2] << 16
                            | bytes[cursor + 3] << 24;
                cursor += 4;
                return value;
            }
        }

        private static byte ReadByte(byte[] bytes, ref int cursor)
        {
            if (cursor >= bytes.Length)
            {
                throw new InvalidDataException(
                    "Forced scene transition payload is truncated.");
            }

            return bytes[cursor++];
        }
    }
}
