using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HollowKnightTAS.Core.Ipc
{
    public enum StartupProfileAttestationStatus
    {
        Unverified,
        Pending,
        Verified,
        Faulted
    }

    public enum StartupRecordingRootStatus
    {
        Unavailable,
        Pending,
        Verified,
        Faulted
    }

    public static class StartupProfileContract
    {
        public const string CommandLineRunPrefix =
            "--hktas-reference-run=";
        public const string RunIdEnvironment = "HKTAS_CLOCK_RUN_ID";
        public const string StartupLatchEnvironment =
            "HKTAS_CLOCK_STARTUP_LATCH";
        public const string PayloadReadyEventPrefix =
            "HollowKnightTAS.T24.ClockPayloadReady.";
        public const string RandomSynchronizationEventPrefix =
            "HollowKnightTAS.T24.RngSync.";
        public const string ProfileId =
            "external-unity-startup-continuous-clock-v40-native-scene-lifecycle";
        public const string RandomSynchronizationPolicyId =
            "unity-init-state-at-root-only-native-scene-lifecycle-v19";
        public const int RandomSynchronizationSeed = 1212896321;
        public const uint BridgeAbi = 10u;
        public const int ReadyBridgeStatus = 2;
        public const float RecordingAbsoluteTime = 768f;
        public const int RecordingFramePhaseModulo = 4;
        public const int RecordingFramePhaseTarget = 0;
    }

    public sealed class StartupProfileAttestation
    {
        private static readonly string[] FieldNames =
        {
            "bridgeAbi",
            "bridgeStatus",
            "deterministicClockAdvanceFaultCode",
            "deterministicClockEnableFaultCode",
            "deterministicClockEnabled",
            "deterministicClockUnityFrameFaultCode",
            "playerLoopBoundaryError",
            "processId",
            "processStartTimeUtcTicks",
            "profile",
            "randomSynchronizationFaultCode",
            "randomSynchronizationSnapshotAvailable",
            "randomSynchronizationResetCount",
            "realtimeEpochNormalizationApplied",
            "realtimeEpochNormalizationFaultCode",
            "recordingPhaseNormalizationCompleted",
            "recordingPhaseNormalizationFaultCode",
            "recordingRandomSynchronizationApplied",
            "requestId",
            "rootStatus",
            "runId",
            "runtimeVirtualClockRegistered",
            "startupFaultCode",
            "startupHookInstalled",
            "startupLatchEnabled",
            "status",
            "timeUpdateResumeBoundaryInstalled",
            "timeUpdateResumeCommitFaultCode"
        };

        public StartupProfileAttestation(
            StartupProfileAttestationStatus status,
            StartupRecordingRootStatus rootStatus,
            string runId,
            int processId,
            long processStartTimeUtcTicks,
            string profile,
            uint bridgeAbi,
            int bridgeStatus,
            bool startupHookInstalled,
            bool startupLatchEnabled,
            bool deterministicClockEnabled,
            bool runtimeVirtualClockRegistered,
            bool timeUpdateResumeBoundaryInstalled,
            int startupFaultCode,
            int deterministicClockEnableFaultCode,
            int deterministicClockAdvanceFaultCode,
            int deterministicClockUnityFrameFaultCode,
            int timeUpdateResumeCommitFaultCode,
            string playerLoopBoundaryError,
            bool randomSynchronizationSnapshotAvailable,
            int randomSynchronizationResetCount,
            bool recordingRandomSynchronizationApplied,
            bool realtimeEpochNormalizationApplied,
            bool recordingPhaseNormalizationCompleted,
            int randomSynchronizationFaultCode,
            int realtimeEpochNormalizationFaultCode,
            int recordingPhaseNormalizationFaultCode)
        {
            if (processId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(processId));
            }
            if (processStartTimeUtcTicks <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(processStartTimeUtcTicks));
            }
            if (!string.IsNullOrEmpty(runId)
                && !IpcIdentifier.IsValid(runId, 96))
            {
                throw new ArgumentException(
                    "Startup run ID is invalid.",
                    nameof(runId));
            }

            Status = status;
            RootStatus = rootStatus;
            RunId = runId ?? string.Empty;
            ProcessId = processId;
            ProcessStartTimeUtcTicks = processStartTimeUtcTicks;
            Profile = profile ?? string.Empty;
            BridgeAbi = bridgeAbi;
            BridgeStatus = bridgeStatus;
            StartupHookInstalled = startupHookInstalled;
            StartupLatchEnabled = startupLatchEnabled;
            DeterministicClockEnabled = deterministicClockEnabled;
            RuntimeVirtualClockRegistered = runtimeVirtualClockRegistered;
            TimeUpdateResumeBoundaryInstalled =
                timeUpdateResumeBoundaryInstalled;
            StartupFaultCode = startupFaultCode;
            DeterministicClockEnableFaultCode =
                deterministicClockEnableFaultCode;
            DeterministicClockAdvanceFaultCode =
                deterministicClockAdvanceFaultCode;
            DeterministicClockUnityFrameFaultCode =
                deterministicClockUnityFrameFaultCode;
            TimeUpdateResumeCommitFaultCode =
                timeUpdateResumeCommitFaultCode;
            PlayerLoopBoundaryError = playerLoopBoundaryError
                                      ?? string.Empty;
            RandomSynchronizationSnapshotAvailable =
                randomSynchronizationSnapshotAvailable;
            RandomSynchronizationResetCount =
                randomSynchronizationResetCount;
            RecordingRandomSynchronizationApplied =
                recordingRandomSynchronizationApplied;
            RealtimeEpochNormalizationApplied =
                realtimeEpochNormalizationApplied;
            RecordingPhaseNormalizationCompleted =
                recordingPhaseNormalizationCompleted;
            RandomSynchronizationFaultCode =
                randomSynchronizationFaultCode;
            RealtimeEpochNormalizationFaultCode =
                realtimeEpochNormalizationFaultCode;
            RecordingPhaseNormalizationFaultCode =
                recordingPhaseNormalizationFaultCode;
        }

        public StartupProfileAttestationStatus Status { get; }
        public StartupRecordingRootStatus RootStatus { get; }
        public string RunId { get; }
        public int ProcessId { get; }
        public long ProcessStartTimeUtcTicks { get; }
        public string Profile { get; }
        public uint BridgeAbi { get; }
        public int BridgeStatus { get; }
        public bool StartupHookInstalled { get; }
        public bool StartupLatchEnabled { get; }
        public bool DeterministicClockEnabled { get; }
        public bool RuntimeVirtualClockRegistered { get; }
        public bool TimeUpdateResumeBoundaryInstalled { get; }
        public int StartupFaultCode { get; }
        public int DeterministicClockEnableFaultCode { get; }
        public int DeterministicClockAdvanceFaultCode { get; }
        public int DeterministicClockUnityFrameFaultCode { get; }
        public int TimeUpdateResumeCommitFaultCode { get; }
        public string PlayerLoopBoundaryError { get; }
        public bool RandomSynchronizationSnapshotAvailable { get; }
        public int RandomSynchronizationResetCount { get; }
        public bool RecordingRandomSynchronizationApplied { get; }
        public bool RealtimeEpochNormalizationApplied { get; }
        public bool RecordingPhaseNormalizationCompleted { get; }
        public int RandomSynchronizationFaultCode { get; }
        public int RealtimeEpochNormalizationFaultCode { get; }
        public int RecordingPhaseNormalizationFaultCode { get; }

        public IReadOnlyDictionary<string, string> ToFields(
            string requestId)
        {
            if (!IpcIdentifier.IsValid(requestId, 128))
            {
                throw new ArgumentException(
                    "Startup attestation request ID is invalid.",
                    nameof(requestId));
            }

            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["bridgeAbi"] = BridgeAbi.ToString(
                    CultureInfo.InvariantCulture),
                ["bridgeStatus"] = BridgeStatus.ToString(
                    CultureInfo.InvariantCulture),
                ["deterministicClockAdvanceFaultCode"] =
                    DeterministicClockAdvanceFaultCode.ToString(
                        CultureInfo.InvariantCulture),
                ["deterministicClockEnableFaultCode"] =
                    DeterministicClockEnableFaultCode.ToString(
                        CultureInfo.InvariantCulture),
                ["deterministicClockEnabled"] = Boolean(
                    DeterministicClockEnabled),
                ["deterministicClockUnityFrameFaultCode"] =
                    DeterministicClockUnityFrameFaultCode.ToString(
                        CultureInfo.InvariantCulture),
                ["playerLoopBoundaryError"] = PlayerLoopBoundaryError,
                ["processId"] = ProcessId.ToString(
                    CultureInfo.InvariantCulture),
                ["processStartTimeUtcTicks"] =
                    ProcessStartTimeUtcTicks.ToString(
                        CultureInfo.InvariantCulture),
                ["profile"] = Profile,
                ["randomSynchronizationFaultCode"] =
                    RandomSynchronizationFaultCode.ToString(
                        CultureInfo.InvariantCulture),
                ["randomSynchronizationSnapshotAvailable"] = Boolean(
                    RandomSynchronizationSnapshotAvailable),
                ["randomSynchronizationResetCount"] =
                    RandomSynchronizationResetCount.ToString(
                        CultureInfo.InvariantCulture),
                ["realtimeEpochNormalizationApplied"] = Boolean(
                    RealtimeEpochNormalizationApplied),
                ["realtimeEpochNormalizationFaultCode"] =
                    RealtimeEpochNormalizationFaultCode.ToString(
                        CultureInfo.InvariantCulture),
                ["recordingPhaseNormalizationCompleted"] = Boolean(
                    RecordingPhaseNormalizationCompleted),
                ["recordingPhaseNormalizationFaultCode"] =
                    RecordingPhaseNormalizationFaultCode.ToString(
                        CultureInfo.InvariantCulture),
                ["recordingRandomSynchronizationApplied"] = Boolean(
                    RecordingRandomSynchronizationApplied),
                ["requestId"] = requestId,
                ["rootStatus"] = RootStatus.ToString(),
                ["runId"] = RunId,
                ["runtimeVirtualClockRegistered"] = Boolean(
                    RuntimeVirtualClockRegistered),
                ["startupFaultCode"] = StartupFaultCode.ToString(
                    CultureInfo.InvariantCulture),
                ["startupHookInstalled"] = Boolean(
                    StartupHookInstalled),
                ["startupLatchEnabled"] = Boolean(
                    StartupLatchEnabled),
                ["status"] = Status.ToString(),
                ["timeUpdateResumeBoundaryInstalled"] = Boolean(
                    TimeUpdateResumeBoundaryInstalled),
                ["timeUpdateResumeCommitFaultCode"] =
                    TimeUpdateResumeCommitFaultCode.ToString(
                        CultureInfo.InvariantCulture)
            };
        }

        public static bool TryParse(
            IReadOnlyDictionary<string, string> fields,
            out StartupProfileAttestation? attestation,
            out string error)
        {
            attestation = null;
            if (fields == null
                || fields.Count != FieldNames.Length
                || FieldNames.Any(name => !fields.ContainsKey(name)))
            {
                error = "Startup attestation has an invalid shape.";
                return false;
            }

            if (!Enum.TryParse(
                    fields["status"],
                    false,
                    out StartupProfileAttestationStatus status)
                || !Enum.TryParse(
                    fields["rootStatus"],
                    false,
                    out StartupRecordingRootStatus rootStatus)
                || !uint.TryParse(
                    fields["bridgeAbi"],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var bridgeAbi)
                || !TryInteger(fields, "bridgeStatus", out var bridgeStatus)
                || !TryInteger(fields, "processId", out var processId)
                || !long.TryParse(
                    fields["processStartTimeUtcTicks"],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var processStartTimeUtcTicks)
                || !TryInteger(
                    fields,
                    "startupFaultCode",
                    out var startupFaultCode)
                || !TryInteger(
                    fields,
                    "deterministicClockEnableFaultCode",
                    out var deterministicClockEnableFaultCode)
                || !TryInteger(
                    fields,
                    "deterministicClockAdvanceFaultCode",
                    out var deterministicClockAdvanceFaultCode)
                || !TryInteger(
                    fields,
                    "deterministicClockUnityFrameFaultCode",
                    out var deterministicClockUnityFrameFaultCode)
                || !TryInteger(
                    fields,
                    "timeUpdateResumeCommitFaultCode",
                    out var timeUpdateResumeCommitFaultCode)
                || !TryInteger(
                    fields,
                    "randomSynchronizationResetCount",
                    out var randomSynchronizationResetCount)
                || !TryInteger(
                    fields,
                    "randomSynchronizationFaultCode",
                    out var randomSynchronizationFaultCode)
                || !TryInteger(
                    fields,
                    "realtimeEpochNormalizationFaultCode",
                    out var realtimeEpochNormalizationFaultCode)
                || !TryInteger(
                    fields,
                    "recordingPhaseNormalizationFaultCode",
                    out var recordingPhaseNormalizationFaultCode)
                || !TryBoolean(
                    fields,
                    "startupHookInstalled",
                    out var startupHookInstalled)
                || !TryBoolean(
                    fields,
                    "startupLatchEnabled",
                    out var startupLatchEnabled)
                || !TryBoolean(
                    fields,
                    "deterministicClockEnabled",
                    out var deterministicClockEnabled)
                || !TryBoolean(
                    fields,
                    "runtimeVirtualClockRegistered",
                    out var runtimeVirtualClockRegistered)
                || !TryBoolean(
                    fields,
                    "timeUpdateResumeBoundaryInstalled",
                    out var timeUpdateResumeBoundaryInstalled)
                || !TryBoolean(
                    fields,
                    "randomSynchronizationSnapshotAvailable",
                    out var randomSynchronizationSnapshotAvailable)
                || !TryBoolean(
                    fields,
                    "recordingRandomSynchronizationApplied",
                    out var recordingRandomSynchronizationApplied)
                || !TryBoolean(
                    fields,
                    "realtimeEpochNormalizationApplied",
                    out var realtimeEpochNormalizationApplied)
                || !TryBoolean(
                    fields,
                    "recordingPhaseNormalizationCompleted",
                    out var recordingPhaseNormalizationCompleted))
            {
                error = "Startup attestation contains invalid values.";
                return false;
            }

            try
            {
                attestation = new StartupProfileAttestation(
                    status,
                    rootStatus,
                    fields["runId"],
                    processId,
                    processStartTimeUtcTicks,
                    fields["profile"],
                    bridgeAbi,
                    bridgeStatus,
                    startupHookInstalled,
                    startupLatchEnabled,
                    deterministicClockEnabled,
                    runtimeVirtualClockRegistered,
                    timeUpdateResumeBoundaryInstalled,
                    startupFaultCode,
                    deterministicClockEnableFaultCode,
                    deterministicClockAdvanceFaultCode,
                    deterministicClockUnityFrameFaultCode,
                    timeUpdateResumeCommitFaultCode,
                    fields["playerLoopBoundaryError"],
                    randomSynchronizationSnapshotAvailable,
                    randomSynchronizationResetCount,
                    recordingRandomSynchronizationApplied,
                    realtimeEpochNormalizationApplied,
                    recordingPhaseNormalizationCompleted,
                    randomSynchronizationFaultCode,
                    realtimeEpochNormalizationFaultCode,
                    recordingPhaseNormalizationFaultCode);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static string Boolean(bool value)
        {
            return value ? "true" : "false";
        }

        private static bool TryBoolean(
            IReadOnlyDictionary<string, string> fields,
            string name,
            out bool value)
        {
            if (string.Equals(fields[name], "true", StringComparison.Ordinal))
            {
                value = true;
                return true;
            }
            if (string.Equals(fields[name], "false", StringComparison.Ordinal))
            {
                value = false;
                return true;
            }

            value = false;
            return false;
        }

        private static bool TryInteger(
            IReadOnlyDictionary<string, string> fields,
            string name,
            out int value)
        {
            return int.TryParse(
                fields[name],
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out value);
        }
    }
}
