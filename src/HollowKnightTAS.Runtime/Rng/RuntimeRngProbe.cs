using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using HollowKnightTAS.Core.Diagnostics;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Rng;
using HollowKnightTAS.Core.Serialization;
using HollowKnightTAS.Runtime.Playback;
using InControl;
using UnityEngine.SceneManagement;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HollowKnightTAS.Runtime.Rng
{
    public sealed class RuntimeRngProbe : IDisposable
    {
        private const int MaximumInMemoryStates = 16384;
        private const int MaximumInMemoryCalls = 4096;

        private readonly RuntimeRngCapabilityResolution capability;
        private readonly RuntimeReplayJournal journal;
        private readonly string sessionId;
        private readonly string outputDirectory;
        private readonly Action<string> logInfo;
        private readonly Action<string> logWarning;
        private readonly Action<string> logError;
        private readonly JsonLinesEventSink sink;
        private readonly List<RngStateRecord> states =
            new List<RngStateRecord>();
        private readonly List<RngCallRecord> calls =
            new List<RngCallRecord>();
        private readonly SortedDictionary<string, long> callSiteCounts =
            new SortedDictionary<string, long>(StringComparer.Ordinal);
        private RngCallSiteHooks? hooks;
        private long sequence;
        private long globalCallCount;
        private long lastSampleMovieTick = long.MinValue;
        private long stateRecordsDropped;
        private long callRecordsDropped;
        private int sceneEpoch;
        private bool diagnosticsEnabled;
        private bool disposed;
        private string fault = string.Empty;

        public RuntimeRngProbe(
            string sessionDirectory,
            string sessionId,
            RuntimeReplayJournal journal,
            RuntimeRngCapabilityResolution capability,
            Action<string> logInfo,
            Action<string> logWarning,
            Action<string> logError)
        {
            if (string.IsNullOrWhiteSpace(sessionDirectory))
            {
                throw new ArgumentException(
                    "A session directory is required.",
                    nameof(sessionDirectory));
            }

            this.sessionId = string.IsNullOrWhiteSpace(sessionId)
                ? throw new ArgumentException(
                    "A session ID is required.",
                    nameof(sessionId))
                : sessionId;
            this.journal = journal
                           ?? throw new ArgumentNullException(nameof(journal));
            this.capability = capability
                              ?? throw new ArgumentNullException(
                                  nameof(capability));
            this.logInfo = logInfo
                           ?? throw new ArgumentNullException(nameof(logInfo));
            this.logWarning = logWarning
                              ?? throw new ArgumentNullException(
                                  nameof(logWarning));
            this.logError = logError
                            ?? throw new ArgumentNullException(nameof(logError));
            outputDirectory = Path.Combine(
                Path.GetFullPath(sessionDirectory),
                "rng");
            Directory.CreateDirectory(outputDirectory);
            WriteResolution();
            sink = new JsonLinesEventSink(
                Path.Combine(outputDirectory, "rng-ledger.jsonl"),
                8192,
                TimeSpan.FromSeconds(2),
                budgetDirectory: Path.GetDirectoryName(sessionDirectory));
            foreach (var site in capability.Whitelist.CallSites)
            {
                callSiteCounts.Add(site.CallSiteId, 0);
            }

            Emit(
                "rng-capability",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["callSiteStatus"] =
                        capability.CallSites.Status.ToString(),
                    ["codecId"] = capability.CodecId,
                    ["codecStatus"] =
                        capability.Codec.Status.ToString(),
                    ["coverage"] = capability.CoverageId,
                    ["hooksAttached"] = HooksAttached ? "true" : "false",
                    ["otherModRng"] = "not-covered",
                    ["systemRandom"] = "not-covered"
                });
            logInfo(
                "T10 RNG capability ready codec="
                + capability.CodecId
                + " coverage="
                + capability.CoverageId
                + " diagnostics=false");
        }

        public RuntimeRngCapabilityResolution Capability => capability;
        public string CodecId => capability.CodecId;
        public string CoverageId => capability.CoverageId;
        public bool StateAvailable => capability.StateAvailable;
        public bool HooksAttached => hooks != null;
        public string Fault => fault;
        public long GlobalCallCount => globalCallCount;
        public long StateRecordsDropped => stateRecordsDropped;
        public long CallRecordsDropped => callRecordsDropped;
        public string LedgerPath =>
            Path.Combine(outputDirectory, "rng-ledger.jsonl");

        public RngStateFingerprint CaptureCurrent()
        {
            if (!StateAvailable)
            {
                throw new InvalidOperationException(
                    "Unity RNG state codec is unavailable: "
                    + capability.Codec.Detail);
            }

            return capability.Codec.Codec!.CaptureCurrent();
        }

        public UnityRandomCodecRoundTripReport VerifyCodecRoundTrip(
            int seed)
        {
            if (!StateAvailable)
            {
                throw new InvalidOperationException(
                    "Unity RNG state codec is unavailable.");
            }

            return capability.Codec.Codec!.VerifyRoundTrip(seed);
        }

        public IReadOnlyList<RngStateRecord> SnapshotStates()
        {
            return new ReadOnlyCollection<RngStateRecord>(
                states.ToList());
        }

        public IReadOnlyList<RngCallRecord> SnapshotCalls()
        {
            return new ReadOnlyCollection<RngCallRecord>(
                calls.ToList());
        }

        public IReadOnlyDictionary<string, long> SnapshotCallSiteCounts()
        {
            return new ReadOnlyDictionary<string, long>(
                new SortedDictionary<string, long>(
                    callSiteCounts,
                    StringComparer.Ordinal));
        }

        internal void EnableDiagnostics()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(RuntimeRngProbe));
            }

            if (diagnosticsEnabled)
            {
                return;
            }

            diagnosticsEnabled = true;
            InputManager.OnUpdate += OnInputManagerUpdated;
            USceneManager.activeSceneChanged += OnActiveSceneChanged;
            if (capability.HooksAvailable)
            {
                hooks = new RngCallSiteHooks(this, capability);
                hooks.Attach();
            }

            Emit(
                "rng-diagnostics-enabled",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["hooksAttached"] = HooksAttached ? "true" : "false"
                });
            logInfo(
                "T10 RNG diagnostics enabled codec="
                + capability.CodecId
                + " coverage="
                + capability.CoverageId
                + " hooks="
                + HooksAttached);
        }

        public void DisableHooksForDiagnosticControl()
        {
            hooks?.Dispose();
            hooks = null;
            Emit(
                "rng-hooks-disabled",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["reason"] = "diagnostic-control-profile"
                });
        }

        internal RngCallScope? TryBeginCall(string callSiteId)
        {
            if (disposed || !string.IsNullOrEmpty(fault))
            {
                return null;
            }

            try
            {
                var before = CaptureCurrent();
                globalCallCount++;
                var siteIndex = checked(callSiteCounts[callSiteId] + 1);
                callSiteCounts[callSiteId] = siteIndex;
                return new RngCallScope(
                    journal.LastCommittedMovieTick,
                    CurrentStamp(TickPhase.RngCallObserved),
                    callSiteId,
                    globalCallCount,
                    siteIndex,
                    before.Sha256);
            }
            catch (Exception exception)
            {
                RecordFault("begin-call", exception);
                return null;
            }
        }

        internal void TryCompleteCall(RngCallScope? scope)
        {
            if (scope == null || disposed)
            {
                return;
            }

            try
            {
                var after = CaptureCurrent();
                var record = new RngCallRecord(
                    scope.MovieTick,
                    scope.TickStamp,
                    scope.CallSiteId,
                    scope.GlobalCallIndex,
                    scope.CallSiteIndex,
                    scope.BeforeStateSha256,
                    after.Sha256);
                AppendBounded(
                    calls,
                    record,
                    MaximumInMemoryCalls,
                    ref callRecordsDropped);
                EmitCall(record);
            }
            catch (Exception exception)
            {
                RecordFault("complete-call", exception);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (diagnosticsEnabled)
            {
                InputManager.OnUpdate -= OnInputManagerUpdated;
                USceneManager.activeSceneChanged -= OnActiveSceneChanged;
                diagnosticsEnabled = false;
            }

            hooks?.Dispose();
            hooks = null;

            try
            {
                EmitSummary();
                sink.Flush(TimeSpan.FromSeconds(2));
            }
            catch (Exception exception)
            {
                logError(
                    "T10 RNG evidence flush failed: "
                    + exception.Message);
            }
            finally
            {
                sink.Dispose();
            }
        }

        private void OnInputManagerUpdated(ulong inputTick, float deltaTime)
        {
            if (!StateAvailable
                || disposed
                || !string.IsNullOrEmpty(fault))
            {
                return;
            }

            var movieTick = journal.LastCommittedMovieTick;
            if (movieTick < 0 || movieTick == lastSampleMovieTick)
            {
                return;
            }

            try
            {
                var fingerprint = CaptureCurrent();
                var record = new RngStateRecord(
                    movieTick,
                    new TickStamp(
                        inputTick,
                        journal.CurrentVisualTick,
                        journal.CurrentFixedTick,
                        sceneEpoch,
                        TickPhase.InControlCommitted),
                    fingerprint.Sha256,
                    globalCallCount,
                    SnapshotCallSiteCounts());
                AppendBounded(
                    states,
                    record,
                    MaximumInMemoryStates,
                    ref stateRecordsDropped);
                lastSampleMovieTick = movieTick;
                EmitState(record);
            }
            catch (Exception exception)
            {
                RecordFault("state-sample", exception);
            }
        }

        private void OnActiveSceneChanged(Scene previous, Scene current)
        {
            sceneEpoch++;
        }

        private TickStamp CurrentStamp(TickPhase phase)
        {
            return new TickStamp(
                InputManager.CurrentTick,
                journal.CurrentVisualTick,
                journal.CurrentFixedTick,
                sceneEpoch,
                phase);
        }

        private void EmitState(RngStateRecord record)
        {
            var fields = CommonStampFields(record.TickStamp);
            fields["callSiteCounts"] = FormatCounts(
                record.CallSiteCounts);
            fields["globalCallCount"] =
                record.GlobalCallCount.ToString(
                    CultureInfo.InvariantCulture);
            fields["movieTick"] = record.MovieTick.ToString(
                CultureInfo.InvariantCulture);
            fields["stateSha256"] = record.StateSha256;
            Emit("rng-state", fields);
        }

        private void EmitCall(RngCallRecord record)
        {
            var fields = CommonStampFields(record.TickStamp);
            fields["afterStateSha256"] = record.AfterStateSha256;
            fields["beforeStateSha256"] = record.BeforeStateSha256;
            fields["callSiteId"] = record.CallSiteId;
            fields["callSiteIndex"] = record.CallSiteIndex.ToString(
                CultureInfo.InvariantCulture);
            fields["globalCallIndex"] =
                record.GlobalCallIndex.ToString(
                    CultureInfo.InvariantCulture);
            fields["movieTick"] = record.MovieTick.ToString(
                CultureInfo.InvariantCulture);
            fields["stateChanged"] = record.StateChanged
                ? "true"
                : "false";
            Emit("rng-call", fields);
        }

        private SortedDictionary<string, string> CommonStampFields(
            TickStamp stamp)
        {
            return new SortedDictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["codecId"] = CodecId,
                ["coverage"] = CoverageId,
                ["fixedTick"] = stamp.FixedTick.ToString(
                    CultureInfo.InvariantCulture),
                ["inputTick"] = stamp.InputTick.ToString(
                    CultureInfo.InvariantCulture),
                ["phase"] = stamp.Phase.ToString(),
                ["sceneEpoch"] = stamp.SceneEpoch.ToString(
                    CultureInfo.InvariantCulture),
                ["visualTick"] = stamp.VisualTick.ToString(
                    CultureInfo.InvariantCulture)
            };
        }

        private void Emit(
            string eventType,
            IReadOnlyDictionary<string, string> fields)
        {
            sink.Emit(
                new StructuredEvent(
                    1,
                    sessionId,
                    Interlocked.Increment(ref sequence),
                    eventType,
                    DateTimeOffset.UtcNow,
                    fields));
        }

        private void EmitSummary()
        {
            Emit(
                "rng-summary",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["callRecordCount"] =
                        calls.Count.ToString(CultureInfo.InvariantCulture),
                    ["callRecordsDropped"] =
                        callRecordsDropped.ToString(
                            CultureInfo.InvariantCulture),
                    ["callSiteCounts"] = FormatCounts(callSiteCounts),
                    ["fault"] = fault,
                    ["globalCallCount"] =
                        globalCallCount.ToString(
                            CultureInfo.InvariantCulture),
                    ["sinkDropped"] =
                        sink.DroppedCount.ToString(
                            CultureInfo.InvariantCulture),
                    ["stateRecordCount"] =
                        states.Count.ToString(CultureInfo.InvariantCulture),
                    ["stateRecordsDropped"] =
                        stateRecordsDropped.ToString(
                            CultureInfo.InvariantCulture)
                });
        }

        private void RecordFault(string operation, Exception exception)
        {
            fault = operation
                    + ":"
                    + exception.GetType().Name
                    + ":"
                    + exception.Message;
            logWarning("T10 RNG diagnostics faulted: " + fault);
            try
            {
                Emit(
                    "rng-fault",
                    new Dictionary<string, string>(
                        StringComparer.Ordinal)
                    {
                        ["detail"] = fault
                    });
            }
            catch (Exception emitException)
            {
                logError(
                    "T10 RNG fault evidence failed: "
                    + emitException.Message);
            }
        }

        private void WriteResolution()
        {
            var builder = new StringBuilder(8192);
            builder.Append('{');
            AppendNumber(builder, "schemaVersion", 1);
            AppendString(
                builder,
                "whitelistId",
                capability.Whitelist.WhitelistId);
            AppendString(builder, "codecId", capability.CodecId);
            AppendString(
                builder,
                "codecStatus",
                capability.Codec.Status.ToString());
            AppendString(
                builder,
                "codecDetail",
                capability.Codec.Detail);
            AppendString(builder, "coverage", capability.CoverageId);
            AppendString(
                builder,
                "callSiteStatus",
                capability.CallSites.Status.ToString());
            AppendString(
                builder,
                "callSiteDetail",
                capability.CallSites.Detail);
            AppendString(
                builder,
                "assemblySha256",
                capability.ActualCallSiteAssembly.Sha256);
            AppendString(
                builder,
                "moduleVersionId",
                capability.ActualCallSiteAssembly.ModuleVersionId);
            AppendBoolean(
                builder,
                "hooksAvailable",
                capability.HooksAvailable);
            AppendString(builder, "systemRandom", "not-covered");
            AppendString(builder, "otherModRng", "not-covered");
            builder.Append(",\"callSites\":[");
            for (var index = 0;
                 index < capability.ActualCallSiteDescriptors.Count;
                 index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var site =
                    capability.ActualCallSiteDescriptors[index];
                builder.Append('{');
                AppendString(builder, "callSiteId", site.CallSiteId);
                AppendString(
                    builder,
                    "declaringType",
                    site.DeclaringType);
                AppendString(
                    builder,
                    "methodSignature",
                    site.MethodSignature);
                AppendNumber(
                    builder,
                    "metadataToken",
                    site.MetadataToken);
                AppendString(
                    builder,
                    "methodIlSha256",
                    site.MethodIlSha256);
                AppendString(builder, "hookType", site.HookType);
                AppendString(
                    builder,
                    "expectedRandomApi",
                    site.ExpectedRandomApi);
                AppendNumber(
                    builder,
                    "expectedRandomCallCount",
                    site.ExpectedRandomCallCount);
                builder.Append('}');
            }

            builder.Append("]}");
            WriteAtomic(
                Path.Combine(
                    outputDirectory,
                    "whitelist-resolution.json"),
                new UTF8Encoding(false, true).GetBytes(
                    builder.ToString()));
        }

        private static void AppendBounded<T>(
            List<T> values,
            T value,
            int maximum,
            ref long dropped)
        {
            if (values.Count == maximum)
            {
                values.RemoveAt(0);
                dropped++;
            }

            values.Add(value);
        }

        private static string FormatCounts(
            IReadOnlyDictionary<string, long> values)
        {
            return string.Join(
                ",",
                values
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(
                        item => item.Key
                                + "="
                                + item.Value.ToString(
                                    CultureInfo.InvariantCulture)));
        }

        private static void AppendString(
            StringBuilder builder,
            string name,
            string value)
        {
            AppendPropertyPrefix(builder, name);
            CanonicalJsonWriter.AppendString(
                builder,
                value ?? string.Empty);
        }

        private static void AppendNumber(
            StringBuilder builder,
            string name,
            long value)
        {
            AppendPropertyPrefix(builder, name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendBoolean(
            StringBuilder builder,
            string name,
            bool value)
        {
            AppendPropertyPrefix(builder, name);
            builder.Append(value ? "true" : "false");
        }

        private static void AppendPropertyPrefix(
            StringBuilder builder,
            string name)
        {
            if (builder[builder.Length - 1] != '{')
            {
                builder.Append(',');
            }

            CanonicalJsonWriter.AppendString(builder, name);
            builder.Append(':');
        }

        private static void WriteAtomic(string path, byte[] bytes)
        {
            var temporary = path
                            + ".tmp-"
                            + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(
                           temporary,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }

    internal sealed class RngCallScope
    {
        public RngCallScope(
            long movieTick,
            TickStamp tickStamp,
            string callSiteId,
            long globalCallIndex,
            long callSiteIndex,
            string beforeStateSha256)
        {
            MovieTick = movieTick;
            TickStamp = tickStamp;
            CallSiteId = callSiteId;
            GlobalCallIndex = globalCallIndex;
            CallSiteIndex = callSiteIndex;
            BeforeStateSha256 = beforeStateSha256;
        }

        public long MovieTick { get; }
        public TickStamp TickStamp { get; }
        public string CallSiteId { get; }
        public long GlobalCallIndex { get; }
        public long CallSiteIndex { get; }
        public string BeforeStateSha256 { get; }
    }

}
