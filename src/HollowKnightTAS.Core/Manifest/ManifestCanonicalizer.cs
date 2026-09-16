using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Serialization;

namespace HollowKnightTAS.Core.Manifest
{
    public static class ManifestCanonicalizer
    {
        public static byte[] Serialize(EnvironmentManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            var builder = new StringBuilder(2048);
            builder.Append('{');
            AppendInt32(builder, "schemaVersion", manifest.SchemaVersion, false);
            AppendString(builder, "gameVersion", manifest.GameVersion);
            AppendString(builder, "moddingApiVersion", manifest.ModdingApiVersion);
            AppendDictionary(builder, "assemblySha256", manifest.AssemblySha256);
            AppendDictionary(builder, "loadedMods", manifest.LoadedMods);
            AppendString(builder, "tasSettingsSha256", manifest.TasSettingsSha256);
            AppendString(builder, "baselineId", manifest.BaselineId);
            AppendString(builder, "baselineSha256", manifest.BaselineSha256);
            AppendString(builder, "operatingSystem", manifest.OperatingSystem);
            AppendString(builder, "architecture", manifest.Architecture);
            AppendString(builder, "locale", manifest.Locale);
            AppendString(builder, "gameLanguage", manifest.GameLanguage);
            AppendInt32(builder, "graphicsQualityLevel", manifest.GraphicsQualityLevel);
            AppendInt32(builder, "screenWidth", manifest.ScreenWidth);
            AppendInt32(builder, "screenHeight", manifest.ScreenHeight);
            AppendString(builder, "fullScreenMode", manifest.FullScreenMode);
            AppendInt32(builder, "targetFrameRate", manifest.TargetFrameRate);
            AppendBoolean(builder, "audioEnabled", manifest.AudioEnabled);
            AppendBoolean(builder, "verificationModeRequested", manifest.VerificationModeRequested);
            AppendBoolean(builder, "verificationModeAllowed", manifest.VerificationModeAllowed);
            if (manifest.SchemaVersion >= 2)
            {
                AppendString(builder, "rngCodecId", manifest.RngCodecId);
                AppendString(builder, "rngCoverage", manifest.RngCoverage);
            }
            AppendList(builder, "unexpectedMods", manifest.UnexpectedMods);
            builder.Append('}');
            return new UTF8Encoding(false, true).GetBytes(builder.ToString());
        }

        public static string ComputeSha256(EnvironmentManifest manifest)
        {
            return Sha256Utility.ComputeHex(Serialize(manifest));
        }

        private static void AppendName(StringBuilder builder, string name, bool comma)
        {
            if (comma)
            {
                builder.Append(',');
            }

            CanonicalJsonWriter.AppendString(builder, name);
            builder.Append(':');
        }

        private static void AppendString(StringBuilder builder, string name, string value)
        {
            AppendName(builder, name, true);
            CanonicalJsonWriter.AppendString(builder, value);
        }

        private static void AppendInt32(StringBuilder builder, string name, int value, bool comma = true)
        {
            AppendName(builder, name, comma);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendBoolean(StringBuilder builder, string name, bool value)
        {
            AppendName(builder, name, true);
            builder.Append(value ? "true" : "false");
        }

        private static void AppendDictionary(
            StringBuilder builder,
            string name,
            IReadOnlyDictionary<string, string> value)
        {
            AppendName(builder, name, true);
            builder.Append('{');
            var first = true;
            foreach (var item in value.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (!first)
                {
                    builder.Append(',');
                }

                CanonicalJsonWriter.AppendString(builder, item.Key);
                builder.Append(':');
                CanonicalJsonWriter.AppendString(builder, item.Value);
                first = false;
            }

            builder.Append('}');
        }

        private static void AppendList(
            StringBuilder builder,
            string name,
            IReadOnlyList<string> value)
        {
            AppendName(builder, name, true);
            builder.Append('[');
            for (var index = 0; index < value.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                CanonicalJsonWriter.AppendString(builder, value[index]);
            }

            builder.Append(']');
        }
    }
}
