using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace HollowKnightTAS.Core.Manifest
{
    public sealed class EnvironmentManifest
    {
        public EnvironmentManifest(
            int schemaVersion,
            string gameVersion,
            string moddingApiVersion,
            IReadOnlyDictionary<string, string> assemblySha256,
            IReadOnlyDictionary<string, string> loadedMods,
            string tasSettingsSha256,
            string baselineId,
            string baselineSha256,
            string operatingSystem,
            string architecture,
            string locale,
            string gameLanguage,
            int graphicsQualityLevel,
            int screenWidth,
            int screenHeight,
            string fullScreenMode,
            int targetFrameRate,
            bool audioEnabled,
            bool verificationModeRequested,
            bool verificationModeAllowed,
            IEnumerable<string> unexpectedMods,
            string rngCodecId = "not-captured",
            string rngCoverage = "none")
        {
            if (schemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            SchemaVersion = schemaVersion;
            GameVersion = Require(gameVersion, nameof(gameVersion));
            ModdingApiVersion = Require(moddingApiVersion, nameof(moddingApiVersion));
            AssemblySha256 = CopyDictionary(assemblySha256, nameof(assemblySha256));
            LoadedMods = CopyDictionary(loadedMods, nameof(loadedMods));
            TasSettingsSha256 = Require(tasSettingsSha256, nameof(tasSettingsSha256));
            BaselineId = Require(baselineId, nameof(baselineId));
            BaselineSha256 = Require(baselineSha256, nameof(baselineSha256));
            OperatingSystem = Require(operatingSystem, nameof(operatingSystem));
            Architecture = Require(architecture, nameof(architecture));
            Locale = Require(locale, nameof(locale));
            GameLanguage = Require(gameLanguage, nameof(gameLanguage));
            GraphicsQualityLevel = graphicsQualityLevel;
            ScreenWidth = screenWidth;
            ScreenHeight = screenHeight;
            FullScreenMode = Require(fullScreenMode, nameof(fullScreenMode));
            TargetFrameRate = targetFrameRate;
            AudioEnabled = audioEnabled;
            VerificationModeRequested = verificationModeRequested;
            VerificationModeAllowed = verificationModeAllowed;
            UnexpectedMods = CopyList(unexpectedMods, nameof(unexpectedMods));
            RngCodecId = Require(rngCodecId, nameof(rngCodecId));
            RngCoverage = Require(rngCoverage, nameof(rngCoverage));
        }

        public int SchemaVersion { get; }
        public string GameVersion { get; }
        public string ModdingApiVersion { get; }
        public IReadOnlyDictionary<string, string> AssemblySha256 { get; }
        public IReadOnlyDictionary<string, string> LoadedMods { get; }
        public string TasSettingsSha256 { get; }
        public string BaselineId { get; }
        public string BaselineSha256 { get; }
        public string OperatingSystem { get; }
        public string Architecture { get; }
        public string Locale { get; }
        public string GameLanguage { get; }
        public int GraphicsQualityLevel { get; }
        public int ScreenWidth { get; }
        public int ScreenHeight { get; }
        public string FullScreenMode { get; }
        public int TargetFrameRate { get; }
        public bool AudioEnabled { get; }
        public bool VerificationModeRequested { get; }
        public bool VerificationModeAllowed { get; }
        public IReadOnlyList<string> UnexpectedMods { get; }
        public string RngCodecId { get; }
        public string RngCoverage { get; }

        private static string Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A non-empty value is required.", name);
            }

            return value;
        }

        private static IReadOnlyDictionary<string, string> CopyDictionary(
            IReadOnlyDictionary<string, string> value,
            string name)
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }

            var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in value)
            {
                var key = Require(item.Key, name);
                result.Add(key, Require(item.Value, name));
            }

            return new ReadOnlyDictionary<string, string>(result);
        }

        private static IReadOnlyList<string> CopyList(IEnumerable<string> value, string name)
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }

            var result = value
                .Select(item => Require(item, name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToList();

            return new ReadOnlyCollection<string>(result);
        }
    }
}
