using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HollowKnightTAS.Core.Automation;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Serialization;

namespace HollowKnightTAS.Runtime.Settings
{
    [Serializable]
    public sealed class TasGlobalSettings
    {
        public bool VerificationModeRequested;
        public string[] AllowedVerificationMods = { "HollowKnightTAS" };
        public int EventQueueCapacity = 4096;
        public int ExitFlushTimeoutMilliseconds = 2000;
        public bool CompanionEnabled = true;
        public bool AutoStartCompanion = true;
        public bool ExitCompanionWithGame;
        public bool CompanionOverlayEnabled = true;
        public int CompanionCommandQueueCapacity = 1024;
        public int CompanionOutboundQueueCapacity = 2048;
        public double CompanionMainThreadBudgetMilliseconds = 2d;
        public int CompanionHandshakeTimeoutMilliseconds = 10000;
        public bool EnableNativeCapabilities;
        public bool InspectorEnabled = true;
        public bool InspectorOverlayEnabled = true;
        public bool InspectorExportEnabled = true;
        public int InspectorSampleEveryMovieTicks = 120;
        public int InspectorExportEverySamples = 2;
        public int InspectorExportQueueCapacity = 2048;
        public bool ReplaySaveEnabled = true;
        public bool ReplaySaveAutoEnabled;
        public long ReplaySaveAutoIntervalMovieTicks = 18000;
        public int ReplaySaveAutoRetentionCount = 20;
        public int DedicatedTasSaveSlot = 4;
        public bool ReplaySaveOverlayEnabled = true;
        // These legacy in-process determinism experiments change the values
        // seen by vanilla gameplay and therefore cannot be enabled by default.
        // T24 requires deterministic control outside the gameplay process (or
        // a proof that it is unnecessary) before either can become eligible.
        public bool ReplaySaveDeterministicTimingEnabled;
        public bool ReplayDeterministicRngEnabled;
        public int ReplayDeterministicRngSeed = 1212896321;
        public bool EnableSemanticKeyframes;
        public string ExternalAutomationMode =
            nameof(AutomationMode.ReadOnly);
        public bool DebugMutationEnabled;

        public void Normalize()
        {
            AllowedVerificationMods = (AllowedVerificationMods ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            if (AllowedVerificationMods.Length == 0)
            {
                AllowedVerificationMods = new[] { "HollowKnightTAS" };
            }

            EventQueueCapacity = Math.Max(128, Math.Min(65536, EventQueueCapacity));
            ExitFlushTimeoutMilliseconds = Math.Max(
                250,
                Math.Min(10000, ExitFlushTimeoutMilliseconds));
            CompanionCommandQueueCapacity = Math.Max(
                64,
                Math.Min(
                    65536,
                    CompanionCommandQueueCapacity));
            CompanionOutboundQueueCapacity = Math.Max(
                64,
                Math.Min(
                    65536,
                    CompanionOutboundQueueCapacity));
            if (double.IsNaN(
                    CompanionMainThreadBudgetMilliseconds)
                || double.IsInfinity(
                    CompanionMainThreadBudgetMilliseconds))
            {
                CompanionMainThreadBudgetMilliseconds = 2d;
            }

            CompanionMainThreadBudgetMilliseconds = Math.Max(
                0.1d,
                Math.Min(
                    2d,
                    CompanionMainThreadBudgetMilliseconds));
            CompanionHandshakeTimeoutMilliseconds = Math.Max(
                1000,
                Math.Min(
                    10000,
                    CompanionHandshakeTimeoutMilliseconds));
            InspectorSampleEveryMovieTicks = Math.Max(
                1,
                Math.Min(1000, InspectorSampleEveryMovieTicks));
            InspectorExportEverySamples = Math.Max(
                1,
                Math.Min(1000, InspectorExportEverySamples));
            InspectorExportQueueCapacity = Math.Max(
                64,
                Math.Min(65536, InspectorExportQueueCapacity));
            ReplaySaveAutoIntervalMovieTicks = Math.Max(
                1,
                Math.Min(
                    1000000000,
                    ReplaySaveAutoIntervalMovieTicks));
            ReplaySaveAutoRetentionCount = Math.Max(
                1,
                Math.Min(1000, ReplaySaveAutoRetentionCount));
            DedicatedTasSaveSlot = Math.Max(
                1,
                Math.Min(4, DedicatedTasSaveSlot));
            ExternalAutomationMode = AutomationModeCodec
                .Normalize(ExternalAutomationMode)
                .ToString();
        }

        public string ComputeCanonicalSha256()
        {
            Normalize();
            var builder = new StringBuilder(256);
            builder.Append("{\"allowedVerificationMods\":[");
            for (var index = 0; index < AllowedVerificationMods.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                CanonicalJsonWriter.AppendString(builder, AllowedVerificationMods[index]);
            }

            builder.Append("],\"eventQueueCapacity\":");
            builder.Append(EventQueueCapacity.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"autoStartCompanion\":");
            builder.Append(AutoStartCompanion ? "true" : "false");
            builder.Append(",\"companionCommandQueueCapacity\":");
            builder.Append(
                CompanionCommandQueueCapacity.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"companionEnabled\":");
            builder.Append(CompanionEnabled ? "true" : "false");
            builder.Append(",\"companionHandshakeTimeoutMilliseconds\":");
            builder.Append(
                CompanionHandshakeTimeoutMilliseconds.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"companionMainThreadBudgetMilliseconds\":");
            builder.Append(
                CompanionMainThreadBudgetMilliseconds.ToString(
                    "R",
                    CultureInfo.InvariantCulture));
            builder.Append(",\"companionOutboundQueueCapacity\":");
            builder.Append(
                CompanionOutboundQueueCapacity.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"companionOverlayEnabled\":");
            builder.Append(CompanionOverlayEnabled ? "true" : "false");
            builder.Append(",\"exitCompanionWithGame\":");
            builder.Append(ExitCompanionWithGame ? "true" : "false");
            builder.Append(",\"enableNativeCapabilities\":");
            builder.Append(
                EnableNativeCapabilities ? "true" : "false");
            builder.Append(",\"exitFlushTimeoutMilliseconds\":");
            builder.Append(ExitFlushTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"inspectorEnabled\":");
            builder.Append(InspectorEnabled ? "true" : "false");
            builder.Append(",\"inspectorExportEnabled\":");
            builder.Append(InspectorExportEnabled ? "true" : "false");
            builder.Append(",\"inspectorExportEverySamples\":");
            builder.Append(
                InspectorExportEverySamples.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"inspectorExportQueueCapacity\":");
            builder.Append(
                InspectorExportQueueCapacity.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"inspectorOverlayEnabled\":");
            builder.Append(InspectorOverlayEnabled ? "true" : "false");
            builder.Append(",\"inspectorSampleEveryMovieTicks\":");
            builder.Append(
                InspectorSampleEveryMovieTicks.ToString(
                    CultureInfo.InvariantCulture));
            // Save scheduling/retention never defines simulated game behavior.
            // Changing it must not make an otherwise compatible movie unreadable.
            builder.Append(",\"autoSavePolicyIdentity\":\"operational-not-simulation-v1\"");
            builder.Append(",\"replaySaveEnabled\":");
            builder.Append(ReplaySaveEnabled ? "true" : "false");
            builder.Append(",\"replaySaveDeterministicTimingEnabled\":");
            builder.Append(
                ReplaySaveDeterministicTimingEnabled ? "true" : "false");
            builder.Append(",\"replayDeterministicRngEnabled\":");
            builder.Append(
                ReplayDeterministicRngEnabled ? "true" : "false");
            builder.Append(",\"replayDeterministicRngSeed\":");
            builder.Append(
                ReplayDeterministicRngSeed.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"replaySaveOverlayEnabled\":");
            builder.Append(ReplaySaveOverlayEnabled ? "true" : "false");
            builder.Append(",\"enableSemanticKeyframes\":");
            builder.Append(
                EnableSemanticKeyframes ? "true" : "false");
            builder.Append(",\"externalAutomationMode\":");
            CanonicalJsonWriter.AppendString(
                builder,
                ExternalAutomationMode);
            builder.Append(",\"debugMutationEnabled\":");
            builder.Append(
                DebugMutationEnabled ? "true" : "false");
            builder.Append(",\"dedicatedTasSaveSlot\":");
            builder.Append(
                DedicatedTasSaveSlot.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"verificationModeRequested\":");
            builder.Append(VerificationModeRequested ? "true" : "false");
            builder.Append('}');
            return Sha256Utility.ComputeUtf8Hex(builder.ToString());
        }

        public TasGlobalSettings CloneNormalized()
        {
            Normalize();
            var result = new TasGlobalSettings
            {
                VerificationModeRequested = VerificationModeRequested,
                AllowedVerificationMods = (string[])AllowedVerificationMods.Clone(),
                EventQueueCapacity = EventQueueCapacity,
                ExitFlushTimeoutMilliseconds = ExitFlushTimeoutMilliseconds,
                CompanionEnabled = CompanionEnabled,
                AutoStartCompanion = AutoStartCompanion,
                ExitCompanionWithGame = ExitCompanionWithGame,
                CompanionOverlayEnabled = CompanionOverlayEnabled,
                CompanionCommandQueueCapacity =
                    CompanionCommandQueueCapacity,
                CompanionOutboundQueueCapacity =
                    CompanionOutboundQueueCapacity,
                CompanionMainThreadBudgetMilliseconds =
                    CompanionMainThreadBudgetMilliseconds,
                CompanionHandshakeTimeoutMilliseconds =
                    CompanionHandshakeTimeoutMilliseconds,
                EnableNativeCapabilities =
                    EnableNativeCapabilities,
                InspectorEnabled = InspectorEnabled,
                InspectorOverlayEnabled = InspectorOverlayEnabled,
                InspectorExportEnabled = InspectorExportEnabled,
                InspectorSampleEveryMovieTicks =
                    InspectorSampleEveryMovieTicks,
                InspectorExportEverySamples =
                    InspectorExportEverySamples,
                InspectorExportQueueCapacity =
                    InspectorExportQueueCapacity,
                ReplaySaveEnabled = ReplaySaveEnabled,
                ReplaySaveAutoEnabled = ReplaySaveAutoEnabled,
                ReplaySaveAutoIntervalMovieTicks =
                    ReplaySaveAutoIntervalMovieTicks,
                ReplaySaveAutoRetentionCount =
                    ReplaySaveAutoRetentionCount,
                DedicatedTasSaveSlot = DedicatedTasSaveSlot,
                ReplaySaveOverlayEnabled = ReplaySaveOverlayEnabled,
                ReplaySaveDeterministicTimingEnabled =
                    ReplaySaveDeterministicTimingEnabled,
                ReplayDeterministicRngEnabled =
                    ReplayDeterministicRngEnabled,
                ReplayDeterministicRngSeed =
                    ReplayDeterministicRngSeed,
                EnableSemanticKeyframes =
                    EnableSemanticKeyframes,
                ExternalAutomationMode =
                    ExternalAutomationMode,
                DebugMutationEnabled =
                    DebugMutationEnabled
            };
            result.Normalize();
            return result;
        }
    }
}
