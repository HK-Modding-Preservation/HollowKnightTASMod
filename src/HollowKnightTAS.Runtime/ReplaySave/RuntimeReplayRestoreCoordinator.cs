using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GlobalEnums;
using HollowKnightTAS.Core.Control;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Ipc;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.Playback;
using HollowKnightTAS.Core.ReplaySave;
using HollowKnightTAS.Core.State;
using HollowKnightTAS.Runtime.Control;
using HollowKnightTAS.Runtime.Companion;
using HollowKnightTAS.Runtime.Input;
using HollowKnightTAS.Runtime.Playback;
using HollowKnightTAS.Runtime.State;
using InControl;
using Modding;
using UnityEngine;

namespace HollowKnightTAS.Runtime.ReplaySave
{
    public sealed class RuntimeReplayRestoreCoordinator : IDisposable
    {
        private const float LoadTimeoutSeconds = 90f;
        private const float BaselineAlignmentTimeoutSeconds = 30f;
        private const int BaselineStableMatchCount = 2;
        private const float ColdRecordingRootTimeoutSeconds = 900f;

        private readonly IReplaySaveStore store;
        private readonly DesktopSaveSlotBaselineProvider baselineProvider;
        private readonly int dedicatedTasSlot;
        private readonly string sessionId;
        private readonly string manifestSha256;
        private readonly RuntimeReplayJournal journal;
        private readonly Action<string> logInfo;
        private readonly Action<string> logWarning;
        private readonly Action<string> logError;
        private readonly IReplayRestoreAccelerator? accelerator;
        private readonly RuntimeSnapshotCapture snapshotCapture =
            ReplaySaveSnapshotCapture.Create();
        private readonly RuntimeSnapshotCapture targetSnapshotCapture =
            ReplaySaveSnapshotCapture.CreateTarget();
        private CancellationTokenSource cancellation =
            new CancellationTokenSource();
        private RuntimeReplayRestoreRunner? runner;
        private Task<ReplaySaveLoadResult>? loadTask;
        private Task<ReplayLifecycleExecutionPlan>? rootPlanLoadTask;
        private BaselineBundle? rootPlanBaseline;
        private BaselineBundle RestoreBaseline => rootPlanBaseline ?? package!.Baseline;
        private string RestoreSourceId => coldIntent?.IsRootPlanSource == true
            ? "branch-" + lifecycleExecutionPlan!.SourceBranchId : package!.Descriptor.ReplaySaveId;
        private Task<ReplaySaveMovieObjectLoadResult>?
            sourceMovieLoadTask;
        private Task<ReplaySaveMovieObjectLoadResult>?
            targetMovieLoadTask;
        private ReplaySaveLoadResult? loadResult;
        private ReplaySavePackage? package;
        private MovieDocument? targetMovie;
        private MovieDocument? targetReplayPrefix;
        private ReplayLifecycleExecutionPlan? lifecycleExecutionPlan;
        private IReadOnlyDictionary<string, byte[]>? lifecycleExecutionObjects;
        private ReplayRestoreHandle handle;
        private ReplayRestorePhase phase;
        private ReplaySaveStatus status;
        private string detail = string.Empty;
        private BaselineInstallPlan? installPlan;
        private DedicatedTasSlotLease? slotLease;
        private DedicatedTasSlotLease? lifecycleSlotLease;
        private RestoreSettingsLease? settingsLease;
        private RuntimePauseController? pauseController;
        private RuntimePlaybackController? playbackController;
        private RestoreMovieTickGate? replayGate;
        private ReplayLifecycleCursor? replayLifecycle;
        private Stopwatch? replayLifecycleElapsed;
        private bool replayNativeCommitted;
        private bool replayNativeCompleted;
        private bool replayNativeStarted;
        private bool replayNativeLoadHookAttached;
        private bool replayNativeLoadSucceeded;
        private int replayNativeCallbackFrame;
        private bool disposeAfterLifecycle;
        private ReplaySaveStatus? pendingLifecycleTermination;
        private string pendingLifecycleTerminationReason = string.Empty;
        private bool disposed;
        private bool loadCallbackReceived;
        private bool loadCallbackSuccess;
        private bool playbackStopped;
        private PlaybackStopReason? playbackStopReason;
        private BindingRestoreReport? bindingRestore;
        private bool? heroControlRestoreEquivalent;
        private bool acceleratorVerificationPending;
        private bool targetCaptured;
        private bool targetHashMatched;
        private string actualTargetSha256 = string.Empty;
        private string expectedVerificationSha256 = string.Empty;
        private string actualVerificationSha256 = string.Empty;
        private bool? strictTargetHashMatched;
        private long currentMovieTick = -1;
        private long visualTick;
        private long fixedTick;
        private int sceneEpoch;
        private float phaseStartedRealtime;
        private float menuReadyAtRealtime = -1f;
        private bool returnToMenuRequested;
        private int consecutiveBaselineMatches;
        private bool journalPlaybackCaptureActive;
        private string journalPlaybackCaptureError = string.Empty;
        private Action? coldBoundaryCommandPump;
        private Func<
            RuntimePauseController,
            RestoreSettingsLease,
            MovieDocument,
            ControlResult>? coldTargetHandoff;
        private RuntimeStartupProfileAttestor? startupAttestor;
        private RuntimeRecordingRootCoordinator? recordingRootCoordinator;
        private int lastColdBaselineFrame = -1;
        private bool coldRootRequestSignaled => recordingRootCoordinator?.RootRequested == true;
        private ColdRestoreIntent? coldIntent;
        private ReplayRestoreStrategy restoreStrategy =
            ReplayRestoreStrategy.FunctionalReplayRestore;
        private ReplayRestoreEquivalenceClass equivalenceClass =
            ReplayRestoreEquivalenceClass.FunctionalOnly;

        public RuntimeReplayRestoreCoordinator(
            IReplaySaveStore store,
            DesktopSaveSlotBaselineProvider baselineProvider,
            int dedicatedTasSlot,
            string sessionId,
            string manifestSha256,
            RuntimeReplayJournal journal,
            Action<string> logInfo,
            Action<string> logWarning,
            Action<string> logError,
            IReplayRestoreAccelerator? accelerator = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.baselineProvider = baselineProvider
                                    ?? throw new ArgumentNullException(
                                        nameof(baselineProvider));
            if (dedicatedTasSlot <= 0 || dedicatedTasSlot > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(dedicatedTasSlot));
            }

            this.dedicatedTasSlot = dedicatedTasSlot;
            this.sessionId = Require(sessionId, nameof(sessionId));
            this.manifestSha256 = Require(
                manifestSha256,
                nameof(manifestSha256));
            this.journal = journal
                           ?? throw new ArgumentNullException(nameof(journal));
            this.logInfo = logInfo ?? throw new ArgumentNullException(nameof(logInfo));
            this.logWarning = logWarning
                              ?? throw new ArgumentNullException(nameof(logWarning));
            this.logError = logError ?? throw new ArgumentNullException(nameof(logError));
            this.accelerator = accelerator;
        }

        public bool IsActive =>
            phase != 0
            && phase != ReplayRestorePhase.Completed
            && phase != ReplayRestorePhase.Cancelled
            && phase != ReplayRestorePhase.Failed;
        public bool RuntimePumpActive => runner != null;
        public bool ColdBoundaryActive =>
            restoreStrategy
            == ReplayRestoreStrategy.VanillaEquivalentColdReplay
            && pauseController?.Mode == SimulationControlMode.Paused
            && (phase == ReplayRestorePhase.BaselineReady
                || phase == ReplayRestorePhase.PausedAtTarget);

        private long EffectiveTargetMovieTick =>
            coldIntent?.TargetMovieTick
            ?? package?.Descriptor.EffectiveMovieTick
            ?? -1;

        private ReplayRestoreTargetVerification TargetVerification =>
            coldIntent?.RequiresExactTargetSemantic == false
                ? ReplayRestoreTargetVerification
                    .ReconstructedObservation
                : ReplayRestoreTargetVerification.ExactSavedSemantic;

        public void ConfigureColdBoundaryCommandPump(Action commandPump)
        {
            ThrowIfDisposed();
            if (IsActive)
            {
                throw new InvalidOperationException(
                    "Cold-boundary command pump cannot change during restore.");
            }

            coldBoundaryCommandPump = commandPump
                                      ?? throw new ArgumentNullException(
                                          nameof(commandPump));
        }

        public void ConfigureColdTargetHandoff(
            Func<
                RuntimePauseController,
                RestoreSettingsLease,
                MovieDocument,
                ControlResult> handoff)
        {
            ThrowIfDisposed();
            if (IsActive)
            {
                throw new InvalidOperationException(
                    "Cold-target handoff cannot change during restore.");
            }

            coldTargetHandoff = handoff
                                ?? throw new ArgumentNullException(
                                    nameof(handoff));
        }

        public void ConfigureStartupProfileAttestor(
            RuntimeStartupProfileAttestor attestor)
        {
            ThrowIfDisposed();
            if (IsActive)
            {
                throw new InvalidOperationException(
                    "Startup profile attestor cannot change during restore.");
            }

            startupAttestor = attestor
                              ?? throw new ArgumentNullException(
                                  nameof(attestor));
        }

        public void ValidateColdBaselineBeforeHandoff(BaselineBundle baseline)
        {
            if (pendingSourceSlotPlan != null)
                throw new SourceSlotApprovalRequiredException("Confirm or cancel the pending source TAS slot proposal before requesting another restore.");
            var plan = baselineProvider.PlanInstall(baseline, dedicatedTasSlot);
            if (plan.RequiresOverwriteApproval)
            {
                pendingSourceSlotPlan = plan;
                sourceSlotProposalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2);
                throw new SourceSlotApprovalRequiredException(
                    "ColdRestoreSlotApprovalRequired: dedicated TAS slot " + dedicatedTasSlot
                    + " contains different bytes. Source-side backup and explicit overwrite approval"
                    + " are required before creating a restart intent; the current game remains open.");
            }
        }

        private BaselineInstallPlan? pendingSourceSlotPlan;
        private ReplaySlotOverwriteAuthorization? pendingLifecycleConsent;
        private ReplaySlotOverwriteAuthorization? approvedLifecycleConsent;
        private DateTimeOffset lifecycleConsentExpiresAt;
        private ReplaySlotOverwriteAuthorization? activeLifecycleConsent;

        public string ValidateColdLifecycleBeforeHandoff(BaselineBundle baseline, string baselineHash,
            string movieHash, string lifecycleHash, long targetTick,
            IEnumerable<ReplayLifecycleRecord> records, IReadOnlyDictionary<string, byte[]> objects)
        {
            var slots = new Dictionary<int, ReplaySlotFileIdentity>();
            foreach (var record in records.Where(x => x.Kind == ReplayLifecycleKind.LoadSlot
                && x.AfterMovieTick <= targetTick && x.Slot != dedicatedTasSlot))
            {
                var plan = baselineProvider.PlanLifecycleInstall(baseline, record, objects);
                if (!plan.RequiresOverwriteApproval) continue;
                var identity = new ReplaySlotFileIdentity(record.Slot,
                    plan.CurrentSaveData == null ? string.Empty : Sha256Utility.ComputeHex(plan.CurrentSaveData),
                    plan.CurrentModdedSaveData == null ? string.Empty : Sha256Utility.ComputeHex(plan.CurrentModdedSaveData));
                if (slots.TryGetValue(record.Slot, out var earlier) && !earlier.Matches(plan.CurrentSaveData, plan.CurrentModdedSaveData))
                    throw new InvalidOperationException("Lifecycle slot changed while preparing overwrite consent.");
                slots[record.Slot] = identity;
            }
            if (slots.Count == 0) { pendingLifecycleConsent = null; return string.Empty; }
            var proposal = new ReplaySlotOverwriteAuthorization(baselineHash, movieHash, lifecycleHash, targetTick, slots.Values);
            if (approvedLifecycleConsent != null && DateTimeOffset.UtcNow < lifecycleConsentExpiresAt
                && approvedLifecycleConsent.Serialize() == proposal.Serialize())
                return proposal.Serialize();
            approvedLifecycleConsent = null;
            pendingLifecycleConsent = proposal;
            lifecycleConsentExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2);
            throw new SourceSlotApprovalRequiredException("ColdRestoreSlotApprovalRequired: lifecycle slots "
                + string.Join(",", slots.Keys.OrderBy(x => x))
                + " require temporary replacement. Approve this exact execution and current file hashes, then retry. No slot files were changed.");
        }
        private DateTimeOffset sourceSlotProposalExpiresAt;
        public bool HasPendingSourceSlotApproval => pendingSourceSlotPlan != null || pendingLifecycleConsent != null;
        public string SourceSlotApprovalDetail => pendingLifecycleConsent != null
            ? "Lifecycle temporary overwrite: slots=" + string.Join(",", pendingLifecycleConsent.Slots.Select(x => x.Slot))
                + "; targetTick=" + pendingLifecycleConsent.TargetTick + "; execution=" + pendingLifecycleConsent.LifecycleSha256
                + "; expires=" + lifecycleConsentExpiresAt.ToString("O")
                + ". Approval writes no files; replay backs up and restores each slot."
            : pendingSourceSlotPlan == null ? string.Empty
            : "Source TAS slot " + pendingSourceSlotPlan.DedicatedTasSlot
                + "; baseline=" + pendingSourceSlotPlan.Bundle.BaselineId
                + "; approval expires=" + sourceSlotProposalExpiresAt.ToString("O")
                + ". Approve to back up and install these baseline bytes, then request restore again; cancel writes nothing.";

        public string ResolveSourceSlotApproval(bool approved)
        {
            if (pendingLifecycleConsent != null)
            {
                var consent = pendingLifecycleConsent;
                pendingLifecycleConsent = null;
                approvedLifecycleConsent = null;
                if (!approved) return "Lifecycle overwrite cancelled; no files written.";
                if (DateTimeOffset.UtcNow >= lifecycleConsentExpiresAt)
                    throw new InvalidOperationException("Lifecycle overwrite consent expired; request fresh preparation.");
                approvedLifecycleConsent = consent;
                return "Exact lifecycle overwrite consent recorded. Retry restore; current files will be rechecked before restart and before each temporary installation.";
            }
            var plan = pendingSourceSlotPlan ?? throw new InvalidOperationException("No source slot approval is pending.");
            pendingSourceSlotPlan = null;
            if (!approved) return "Source TAS slot proposal cancelled; no files written and no restart requested.";
            if (DateTimeOffset.UtcNow > sourceSlotProposalExpiresAt)
                throw new InvalidOperationException("Source slot approval expired; request restore again to inspect fresh bytes.");
            var result = baselineProvider.Install(plan, UserOverwriteApproval.Approved);
            if (!result.Success) throw new InvalidOperationException(result.Error);
            return "Approved baseline installed with original slot backup at " + result.BackupDirectory
                + ". Request restore again to continue; the game has not been restarted.";
        }

        public ReplayRestoreHandle Begin(string replaySaveId)
        {
            PrepareBegin(replaySaveId);
            restoreStrategy = ReplayRestoreStrategy.FunctionalReplayRestore;
            equivalenceClass = ReplayRestoreEquivalenceClass.FunctionalOnly;
            coldIntent = null;
            handle = CreateFunctionalHandle();
            BeginLoad(replaySaveId);
            return handle;
        }

        public ReplayRestoreHandle BeginCold(
            ColdRestoreIntent intent,
            ColdRestoreBuildFingerprint actualBuild,
            DateTimeOffset nowUtc)
        {
            ThrowIfDisposed();
            if (intent == null)
            {
                throw new ArgumentNullException(nameof(intent));
            }

            if (actualBuild == null)
            {
                throw new ArgumentNullException(nameof(actualBuild));
            }

            if (coldBoundaryCommandPump == null)
            {
                throw new InvalidOperationException(
                    "Cold restore requires a configured paused-boundary command pump.");
            }

            var validation = ColdRestoreIntentValidator.ValidateForClaim(
                intent,
                actualBuild,
                nowUtc);
            if (!validation.Success)
            {
                throw new InvalidOperationException(validation.Detail);
            }

            if (!string.Equals(
                    actualBuild.EnvironmentManifestSha256,
                    manifestSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Cold-restore build fingerprint is not bound to this Runtime manifest.");
            }

            var runtimeAssemblySha256 = Sha256Utility.ComputeFileHex(
                typeof(RuntimeReplayRestoreCoordinator).Assembly.Location);
            if (!string.Equals(
                    actualBuild.RuntimeAssemblySha256,
                    runtimeAssemblySha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Cold-restore Runtime assembly hash does not match the loaded build.");
            }

            using (var process = Process.GetCurrentProcess())
            {
                var processStartedAtUtc = process.StartTime.ToUniversalTime();
                if (process.Id == intent.SourceProcessId
                    && processStartedAtUtc
                    == intent.SourceProcessStartedAtUtc.UtcDateTime)
                {
                    throw new InvalidOperationException(
                        "Vanilla-equivalent cold restore cannot reuse the source game process.");
                }
            }

            PrepareBegin(intent.IsRootPlanSource ? intent.OperationId : intent.ReplaySaveId);
            restoreStrategy =
                ReplayRestoreStrategy.VanillaEquivalentColdReplay;
            equivalenceClass =
                ReplayRestoreEquivalenceClass.VanillaEquivalent;
            coldIntent = intent;
            handle = new ReplayRestoreHandle(intent.OperationId);
            BeginLoad(intent.ReplaySaveId);
            return handle;
        }

        private void PrepareBegin(string replaySaveId)
        {
            ThrowIfDisposed();
            if (IsActive)
            {
                throw new InvalidOperationException(
                    "Only one replay restore may be active.");
            }

            if (IsStartedPhase())
            {
                ResetTerminalState();
            }

            if (!MovieProtocolV1.IsIdentifier(replaySaveId))
            {
                throw new ArgumentException(
                    "A canonical replay-save ID is required.",
                    nameof(replaySaveId));
            }

            EnsureRuntimePump();
        }

        private static ReplayRestoreHandle CreateFunctionalHandle()
        {
            return new ReplayRestoreHandle(
                "restore-"
                + DateTimeOffset.UtcNow.ToString(
                    "yyyyMMddTHHmmssfffffffZ",
                    CultureInfo.InvariantCulture)
                + "-"
                + Guid.NewGuid().ToString("N").Substring(0, 8));
        }

        private void BeginLoad(string replaySaveId)
        {
            phase = ReplayRestorePhase.Validating;
            status = ReplaySaveStatus.Restoring;
            detail = restoreStrategy
                     == ReplayRestoreStrategy.VanillaEquivalentColdReplay
                ? "Validating cold-restore intent and replay-save objects."
                : "Validating replay-save descriptor and objects.";
            phaseStartedRealtime = Time.realtimeSinceStartup;
            settingsLease = new RestoreSettingsLease();
            if (coldIntent?.IsRootPlanSource == true)
                rootPlanLoadTask = Task.Run(() =>
                    (store as IReplayLifecyclePlanStore ?? throw new InvalidOperationException("Execution plan storage unavailable."))
                    .LoadExecutionPlan(coldIntent.LifecyclePlanObjectSha256), cancellation.Token);
            else
                loadTask = Task.Run(() => store.Load(replaySaveId), cancellation.Token);
            if (coldIntent != null)
            {
                if (!coldIntent.IsMenuSource)
                    sourceMovieLoadTask = Task.Run(
                        () => store.LoadMovieObject(coldIntent.SourceMovieObjectSha256),
                        cancellation.Token);
                targetMovieLoadTask = Task.Run(
                    () => store.LoadMovieObject(
                        coldIntent.TargetMovieObjectSha256),
                    cancellation.Token);
            }
        }

        public ReplayRestoreProgress Poll(ReplayRestoreHandle value)
        {
            ThrowIfWrongHandle(value);
            var target = EffectiveTargetMovieTick;
            var fraction = target < 0
                ? 0d
                : phase == ReplayRestorePhase.Paused
                  || phase == ReplayRestorePhase.PausedAtTarget
                  || phase == ReplayRestorePhase.Completed
                    ? 1d
                    : currentMovieTick < 0
                        ? 0d
                        : Math.Min(
                            1d,
                            (currentMovieTick + 1d)
                            / (target + 1d));
            return new ReplayRestoreProgress(
                handle,
                phase,
                status,
                currentMovieTick,
                target,
                phase == ReplayRestorePhase.Paused
                || phase == ReplayRestorePhase.PausedAtTarget
                || phase == ReplayRestorePhase.Completed
                    ? checked(target + 1)
                    : -1,
                phase == ReplayRestorePhase.AwaitingOverwriteApproval,
                fraction,
                detail,
                coldIntent?.TargetSemanticSha256
                ?? package?.Descriptor.SemanticSnapshotSha256
                ?? string.Empty,
                actualTargetSha256,
                bindingRestore == null
                    ? (bool?)null
                    : bindingRestore.Equivalent
                      && heroControlRestoreEquivalent == true,
                settingsLease?.After == null
                    ? (bool?)null
                    : settingsLease.Equivalent,
                ReplaySaveSemanticVerifier.ProjectionId,
                expectedVerificationSha256,
                actualVerificationSha256,
                strictTargetHashMatched,
                restoreStrategy,
                equivalenceClass,
                coldIntent?.OperationId ?? string.Empty,
                TargetVerification);
        }

        public ReplayRestoreResult ApproveOverwrite(
            ReplayRestoreHandle value,
            bool approved)
        {
            ThrowIfWrongHandle(value);
            if (phase != ReplayRestorePhase.AwaitingOverwriteApproval)
            {
                return new ReplayRestoreResult(
                    false,
                    Poll(value),
                    "Restore is not waiting for overwrite approval.");
            }

            if (!approved)
            {
                CancelInternal("User cancelled TAS slot overwrite.");
                return new ReplayRestoreResult(
                    true,
                    Poll(value),
                    string.Empty);
            }

            InstallBaseline(UserOverwriteApproval.Approved);
            return new ReplayRestoreResult(
                phase != ReplayRestorePhase.Failed,
                Poll(value),
                phase == ReplayRestorePhase.Failed ? detail : string.Empty);
        }

        public ReplayRestoreResult Cancel(ReplayRestoreHandle value)
        {
            ThrowIfWrongHandle(value);
            if (phase == ReplayRestorePhase.Paused
                || phase == ReplayRestorePhase.PausedAtTarget)
            {
                return new ReplayRestoreResult(
                    false,
                    Poll(value),
                    "A successful paused restore must be resumed, not cancelled.");
            }

            if (!IsActive)
            {
                return new ReplayRestoreResult(
                    false,
                    Poll(value),
                    "Restore is already terminal.");
            }

            CancelInternal("Restore cancelled by user.");
            return new ReplayRestoreResult(true, Poll(value), string.Empty);
        }

        public ReplayRestoreResult Resume(ReplayRestoreHandle value)
        {
            ThrowIfWrongHandle(value);
            if ((phase != ReplayRestorePhase.Paused
                 && phase != ReplayRestorePhase.PausedAtTarget)
                || pauseController == null)
            {
                return new ReplayRestoreResult(
                    false,
                    Poll(value),
                    "Restore is not paused at a verified target.");
            }

            if (restoreStrategy
                == ReplayRestoreStrategy.VanillaEquivalentColdReplay)
            {
                return CompleteColdTargetHandoff(value);
            }

            var resume = pauseController.Resume();
            if (!resume.Success)
            {
                Fail(
                    ReplaySaveStatus.Failed,
                    "Could not resume verified target: " + resume.Error);
                return new ReplayRestoreResult(false, Poll(value), detail);
            }

            playbackController?.Dispose();
            playbackController = null;
            EndJournalPlaybackCapture();
            pauseController.Dispose();
            pauseController = null;
            if (settingsLease != null && !settingsLease.Restore())
            {
                Fail(
                    ReplaySaveStatus.Failed,
                    "Restore settings were not restored exactly on resume.");
                return new ReplayRestoreResult(false, Poll(value), detail);
            }

            slotLease?.Commit();
            slotLease?.Dispose();
            slotLease = null;
            phase = ReplayRestorePhase.Completed;
            status = ReplaySaveStatus.Ready;
            detail = "Verified restore resumed at movie tick "
                     + checked(EffectiveTargetMovieTick + 1)
                     + ".";
            StopRuntimePump();
            return new ReplayRestoreResult(true, Poll(value), string.Empty);
        }

        private ReplayRestoreResult CompleteColdTargetHandoff(
            ReplayRestoreHandle value)
        {
            if (phase != ReplayRestorePhase.PausedAtTarget
                || pauseController == null
                || pauseController.Mode != SimulationControlMode.Paused
                || settingsLease == null
                || (package == null && !(coldIntent?.IsRootPlanSource == true
                    && rootPlanBaseline != null && lifecycleExecutionPlan != null))
                || targetMovie == null
                || coldTargetHandoff == null)
            {
                return new ReplayRestoreResult(
                    false,
                    Poll(value),
                    "Cold restore is not ready for a zero-tick paused handoff.");
            }

            playbackController?.Dispose();
            playbackController = null;
            EndJournalPlaybackCapture();
            var handoff = coldTargetHandoff(
                pauseController,
                settingsLease,
                targetMovie);
            if (!handoff.Success)
            {
                Fail(
                    ReplaySaveStatus.Failed,
                    "Cold target paused handoff failed: " + handoff.Error);
                return new ReplayRestoreResult(false, Poll(value), detail);
            }

            // Ownership moved to RuntimeControlService while the same
            // completed-frame guard is still blocking the main thread.
            pauseController = null;
            settingsLease = null;
            slotLease?.Commit();
            slotLease?.Dispose();
            slotLease = null;
            phase = ReplayRestorePhase.Completed;
            status = ReplaySaveStatus.Ready;
            detail = "Verified cold restore handed off Paused at movie tick "
                     + EffectiveTargetMovieTick
                     + "; next input tick is " + checked(EffectiveTargetMovieTick + 1)
                     + ". No gameplay frame was advanced by the handoff.";
            StopRuntimePump();
            return new ReplayRestoreResult(true, Poll(value), string.Empty);
        }

        public ReplayRestoreResult ReleaseColdBaseline(
            ReplayRestoreHandle value)
        {
            ThrowIfWrongHandle(value);
            if (restoreStrategy
                != ReplayRestoreStrategy.VanillaEquivalentColdReplay
                || phase != ReplayRestorePhase.BaselineReady
                || pauseController == null
                || pauseController.Mode != SimulationControlMode.Paused)
            {
                return new ReplayRestoreResult(
                    false,
                    Poll(value),
                    "Cold restore is not paused at its verified baseline boundary.");
            }

            recordingRootCoordinator?.Dispose();
            var resume = pauseController.Resume();
            if (!resume.Success)
            {
                Fail(
                    ReplaySaveStatus.Failed,
                    "Could not release cold baseline boundary: "
                    + resume.Error);
                return new ReplayRestoreResult(false, Poll(value), detail);
            }

            StartFullReplay(reusePauseController: true);
            return new ReplayRestoreResult(
                phase != ReplayRestorePhase.Failed,
                Poll(value),
                phase == ReplayRestorePhase.Failed
                    ? detail
                    : string.Empty);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            if (replayNativeCommitted)
            {
                disposeAfterLifecycle = true;
                DeferLifecycleTermination(ReplaySaveStatus.Cancelled, "Replay restore coordinator disposed.");
                return;
            }

            if (IsActive)
            {
                CancelInternal("Replay restore coordinator disposed.");
            }

            disposed = true;
            DetachReplayNativeLoadHook();
            cancellation.Cancel();
            playbackController?.Dispose();
            playbackController = null;
            EndJournalPlaybackCapture();
            pauseController?.Dispose();
            pauseController = null;
            slotLease?.Dispose();
            slotLease = null;
            settingsLease?.Dispose();
            settingsLease = null;
            StopRuntimePump();

            cancellation.Dispose();
        }

        internal void OnUpdate()
        {
            if (disposed || !IsStartedPhase())
            {
                return;
            }

            try
            {
                if (phase == ReplayRestorePhase.Validating
                    && (coldIntent?.IsRootPlanSource == true ? rootPlanLoadTask?.IsCompleted == true : loadTask?.IsCompleted == true)
                    && (coldIntent == null
                        || (coldIntent.IsMenuSource || sourceMovieLoadTask?.IsCompleted == true)
                           && targetMovieLoadTask?.IsCompleted == true))
                {
                    CompleteValidation();
                }

                if ((phase == ReplayRestorePhase.ReturningToMenu
                     || phase == ReplayRestorePhase.LoadingBaseline
                     || phase == ReplayRestorePhase.AligningBaseline)
                    && Time.realtimeSinceStartup - phaseStartedRealtime
                    > (phase == ReplayRestorePhase.AligningBaseline
                       && coldRootRequestSignaled
                        ? ColdRecordingRootTimeoutSeconds
                        : LoadTimeoutSeconds))
                {
                    Fail(
                        ReplaySaveStatus.Failed,
                        phase == ReplayRestorePhase.ReturningToMenu
                            ? "Timed out while returning to the title menu "
                              + "before the dedicated TAS baseline load."
                            : "Timed out while loading the dedicated TAS baseline.");
                }
            }
            catch (Exception exception)
            {
                Fail(
                    ReplaySaveStatus.Failed,
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal void OnFixedUpdate()
        {
            fixedTick++;
        }

        internal void OnLateUpdate()
        {
            if (disposed || !IsStartedPhase())
            {
                return;
            }

            visualTick++;
            try
            {
                if (phase == ReplayRestorePhase.ReturningToMenu)
                {
                    AdvanceReturnToMenu();
                }
                else if (phase == ReplayRestorePhase.LoadingBaseline)
                {
                    AdvanceLoad();
                }
                else if (phase == ReplayRestorePhase.AligningBaseline)
                {
                    if (restoreStrategy != ReplayRestoreStrategy.VanillaEquivalentColdReplay)
                        AdvanceBaselineAlignment();
                }
                else if (phase == ReplayRestorePhase.ReplayingPrefix)
                {
                    AdvanceReplay();
                }
                else if (phase == ReplayRestorePhase.VerifyingTarget)
                {
                    if (acceleratorVerificationPending)
                    {
                        CompleteAcceleratorVerification();
                    }
                    else
                    {
                        CompleteTargetVerification();
                    }
                }
            }
            catch (Exception exception)
            {
                Fail(
                    ReplaySaveStatus.Failed,
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private void CompleteValidation()
        {
            if (coldIntent?.IsRootPlanSource == true)
            {
                lifecycleExecutionPlan = rootPlanLoadTask!.GetAwaiter().GetResult();
                rootPlanLoadTask = null;
                rootPlanBaseline = lifecycleExecutionPlan.ValidateSeek(manifestSha256, coldIntent.TargetMovieTick);
                lifecycleExecutionObjects = lifecycleExecutionPlan.CopySlotObjects();
            }
            else
            {
                var task = loadTask!;
                loadTask = null;
                loadResult = task.GetAwaiter().GetResult();
                if (!loadResult.Success || loadResult.Package == null)
                {
                    Fail(loadResult.Status, "Replay-save validation failed: " + loadResult.Error);
                    return;
                }
                package = loadResult.Package;
            }
            _ = SemanticSnapshotCanonicalizer.Deserialize(
                RestoreBaseline.SemanticSnapshotBytes);
            if (restoreStrategy
                == ReplayRestoreStrategy.VanillaEquivalentColdReplay)
            {
                var targetObject = targetMovieLoadTask!
                    .GetAwaiter()
                    .GetResult();
                targetMovieLoadTask = null;
                var targetMovieBytes = targetObject.Bytes;
                if (!coldIntent!.IsMenuSource)
                {
                    var sourceObject = sourceMovieLoadTask!.GetAwaiter().GetResult();
                    sourceMovieLoadTask = null;
                    if (!sourceObject.Success || sourceObject.Bytes == null)
                    {
                        Fail(ReplaySaveStatus.Corrupt, "Cold source movie object validation failed: " + sourceObject.Error);
                        return;
                    }
                    var sourceMovie = ParseCanonicalMovie(sourceObject.Bytes,
                        coldIntent.SourceMovieObjectSha256, "cold-source.hktas");
                    if (!string.Equals(sourceMovie.Header.ManifestSha256, manifestSha256, StringComparison.Ordinal))
                    {
                        Fail(ReplaySaveStatus.Corrupt, "Persisted source movie is not bound to the current manifest.");
                        return;
                    }
                }

                if (!targetObject.Success || targetMovieBytes == null)
                {
                    Fail(
                        ReplaySaveStatus.Corrupt,
                        "Cold target movie object validation failed: "
                        + targetObject.Error);
                    return;
                }

                targetMovie = ParseCanonicalMovie(
                    targetMovieBytes,
                    coldIntent.TargetMovieObjectSha256,
                    "cold-target.hktas");
                if (!ValidateColdPackageBinding(out var bindingError))
                {
                    Fail(
                        ReplaySaveStatus.Corrupt,
                        "Cold-restore intent binding failed: "
                        + bindingError);
                    return;
                }

                targetReplayPrefix = CreateReplayPrefix(
                    targetMovie,
                    coldIntent.TargetMovieTick);

                phase = ReplayRestorePhase.PlanningAcceleration;
                detail =
                    "Cold restore requires exact baseline replay; accelerators are disabled.";
                PlanBaselineInstall();
                return;
            }


            if (package == null) throw new InvalidOperationException("Checkpoint restore package is unavailable.");
            targetMovie = package.Movie;
            targetReplayPrefix = package.Movie;

            phase = ReplayRestorePhase.PlanningAcceleration;
            detail = "Evaluating optional restore accelerator.";
            var acceleration =
                ReplayRestoreAccelerationPolicy.EvaluatePlan(
                    accelerator,
                    package.Descriptor);

            if (acceleration.Status
                == ReplayRestoreAccelerationStatus.Ready)
            {
                var accelerated =
                    ReplayRestoreAccelerationPolicy.TryRestore(
                        accelerator,
                        acceleration,
                        cancellation.Token);
                if (accelerated.Success)
                {
                    var expectedResumeMovieTick = checked(
                        package.Descriptor.EffectiveMovieTick + 1);
                    if (accelerated.ResumeMovieTick
                        == expectedResumeMovieTick)
                    {
                        BeginAcceleratorVerification();
                        return;
                    }

                    logWarning(
                        "T09 accelerator returned resume movie tick "
                        + accelerated.ResumeMovieTick
                        + " instead of "
                        + expectedResumeMovieTick
                        + "; "
                        + "falling back to clean baseline replay.");
                }
                else
                {
                    logWarning(
                        accelerated.Faulted
                            ? "T09 accelerator faulted; full replay fallback: "
                              + accelerated.Detail
                            : "T09 accelerator declined restore after planning; "
                              + "full replay fallback: "
                              + accelerated.Detail);
                }

                PrepareFullReplayFallback();
                return;
            }

            PlanBaselineInstall();
        }

        private void PlanBaselineInstall()
        {
            installPlan = baselineProvider.PlanInstall(
                RestoreBaseline,
                dedicatedTasSlot);
            slotLease = CreateRecoverableSlotLease(installPlan);
            if (installPlan.RequiresOverwriteApproval)
            {
                if (restoreStrategy
                    == ReplayRestoreStrategy.VanillaEquivalentColdReplay)
                {
                    Fail(
                        ReplaySaveStatus.Failed,
                        "Cold-restore TAS slot bytes differ from the source-approved baseline; refusing a new-process overwrite prompt.");
                    return;
                }

                phase = ReplayRestorePhase.AwaitingOverwriteApproval;
                detail =
                    "Dedicated TAS slot "
                    + dedicatedTasSlot
                    + " contains different bytes; explicit confirmation is required.";
                return;
            }

            InstallBaseline(UserOverwriteApproval.None);
        }

        private bool ValidateColdPackageBinding(out string error)
        {
            var intent = coldIntent
                         ?? throw new InvalidOperationException(
                             "Cold-restore intent is unavailable.");
            if (intent.SlotOverwriteAuthorization.Length != 0)
            {
                var consent = ReplaySlotOverwriteAuthorization.Deserialize(intent.SlotOverwriteAuthorization);
                var lifecycleHash = intent.LifecyclePlanObjectSha256.Length != 0 ? intent.LifecyclePlanObjectSha256
                    : package?.Descriptor.LifecycleObjectSha256;
                if (lifecycleHash == null || !consent.MatchesExecution(intent.BaselineObjectSha256,
                    intent.TargetMovieObjectSha256, lifecycleHash, intent.TargetMovieTick))
                { error = "Slot overwrite consent does not match the loaded lifecycle execution."; return false; }
                activeLifecycleConsent = consent;
            }
            if (intent.IsRootPlanSource)
            {
                var plan = lifecycleExecutionPlan!;
                if (plan.BaselineObjectSha256 != intent.BaselineObjectSha256
                    || new MovieCanonicalWriter().ComputeMovieId(plan.Movie) != intent.TargetMovieObjectSha256
                    || plan.Movie.Header.ManifestSha256 != manifestSha256
                    || intent.PrefixSha256 != MoviePrefixIdentity.ComputeSha256(plan.Movie, 0))
                { error = "Root execution plan does not match the claimed intent."; return false; }
                error = string.Empty;
                return true;
            }
            var descriptor = package!.Descriptor;
            if (intent.LifecyclePlanObjectSha256.Length != 0)
            {
                try
                {
                    if (!(store is IReplayLifecyclePlanStore plans))
                        throw new InvalidDataException("Execution plan storage is unavailable.");
                    var plan = plans.LoadExecutionPlan(intent.LifecyclePlanObjectSha256);
                    if (plan.BaselineObjectSha256 != intent.BaselineObjectSha256
                        || new MovieCanonicalWriter().ComputeMovieId(plan.Movie) != intent.TargetMovieObjectSha256
                        || plan.Movie.Header.ManifestSha256 != manifestSha256)
                        throw new InvalidDataException("Execution plan baseline, movie, or environment binding mismatch.");
                    lifecycleExecutionPlan = plan;
                    lifecycleExecutionObjects = plan.CopySlotObjects();
                }
                catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is InvalidOperationException)
                { error = exception.Message; return false; }
            }
            if (!string.Equals(
                    intent.ReplaySaveId,
                    descriptor.ReplaySaveId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    intent.BuildFingerprint.EnvironmentManifestSha256,
                    descriptor.ManifestSha256,
                    StringComparison.Ordinal)
                || !string.Equals(
                    intent.BaselineObjectSha256,
                    descriptor.BaselineObjectSha256,
                    StringComparison.Ordinal)
                || !string.Equals(
                    intent.JournalHeadSha256,
                    descriptor.JournalHeadSha256,
                    StringComparison.Ordinal))
            {
                error =
                    "Replay-save descriptor, build, baseline, or journal identity does not match the claimed intent.";
                return false;
            }

            var movie = targetMovie
                        ?? throw new InvalidOperationException(
                            "Cold target movie is unavailable.");
            var expandedTicks = CountMovieTicks(movie);
            if ((intent.OperationKind == ColdRestoreOperationKind.RestoreReplaySave
                 && intent.TargetMovieTick < descriptor.EffectiveMovieTick)
                || intent.TargetMovieTick >= expandedTicks)
            {
                error =
                    "Cold target tick is outside the persisted target movie or precedes its selected checkpoint.";
                return false;
            }

            if (!string.Equals(
                    movie.Header.ManifestSha256,
                    descriptor.ManifestSha256,
                    StringComparison.Ordinal)
                || !string.Equals(
                    movie.Header.BaselineId,
                    package.Movie.Header.BaselineId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    movie.Header.BaselineSha256,
                    package.Movie.Header.BaselineSha256,
                    StringComparison.Ordinal))
            {
                error =
                    "Persisted target movie header does not match the selected replay checkpoint baseline.";
                return false;
            }

            // Derived cold seeks replay all target inputs from the baseline.
            // They never apply the selected checkpoint's later world snapshot.
            var checkpointTicks = ColdReplayPrefixPolicy.RequiredTicks(
                intent.OperationKind, descriptor.EffectiveMovieTick);
            var prefixSha256 = MoviePrefixIdentity.ComputeSha256(
                movie,
                checkpointTicks);
            if (!string.Equals(
                    intent.PrefixSha256,
                    prefixSha256,
                    StringComparison.Ordinal)
                || !MoviePrefixIdentity.IsCompatible(
                    package.Movie,
                    movie,
                    checkpointTicks))
            {
                error =
                    "Canonical target movie prefix does not match the selected replay checkpoint or claimed intent.";
                return false;
            }

            if (intent.OperationKind
                == ColdRestoreOperationKind.RestoreReplaySave)
            {
                if (!string.Equals(
                        intent.TargetMovieObjectSha256,
                        descriptor.MovieObjectSha256,
                        StringComparison.Ordinal)
                    || intent.TargetMovieTick
                       != descriptor.EffectiveMovieTick
                    || !string.Equals(
                        intent.TargetSemanticSha256,
                        descriptor.SemanticSnapshotSha256,
                        StringComparison.Ordinal))
                {
                    error =
                        "Replay-save restore target movie, tick, or semantic hash does not match its descriptor.";
                    return false;
                }
            }
            else if (!string.IsNullOrEmpty(intent.TargetSemanticSha256))
            {
                error =
                    "Seek/branch intent must observe its newly reconstructed target instead of claiming a saved semantic hash.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void InstallBaseline(UserOverwriteApproval approval)
        {
            phase = ReplayRestorePhase.InstallingBaseline;
            detail = "Installing exact baseline into dedicated TAS slot.";
            var install = slotLease!.Install(approval);
            if (!install.Success)
            {
                if (install.Cancelled)
                {
                    CancelInternal(install.Error);
                }
                else
                {
                    Fail(ReplaySaveStatus.Failed, install.Error);
                }

                return;
            }

            var manager = GameManager.instance;
            if (manager == null)
            {
                Fail(
                    ReplaySaveStatus.Failed,
                    "GameManager is unavailable for baseline load.");
                return;
            }

            phase = ReplayRestorePhase.ReturningToMenu;
            detail =
                "Returning to the title menu without saving before baseline load.";
            phaseStartedRealtime = Time.realtimeSinceStartup;
            menuReadyAtRealtime = -1f;
            returnToMenuRequested = false;
            AdvanceReturnToMenu();
        }

        private void AdvanceReturnToMenu()
        {
            var manager = GameManager.instance;
            if (manager == null)
            {
                return;
            }

            var sceneName =
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            var titleMenuReady =
                string.Equals(
                    sceneName,
                    "Menu_Title",
                    StringComparison.Ordinal)
                && manager.gameState == GameState.MAIN_MENU
                && !manager.IsInSceneTransition;
            if (titleMenuReady)
            {
                if (menuReadyAtRealtime < 0f)
                {
                    menuReadyAtRealtime = Time.realtimeSinceStartup;
                    detail =
                        "Title menu reached; waiting for lifecycle stabilization.";
                    return;
                }

                if (Time.realtimeSinceStartup - menuReadyAtRealtime >= 0.5f)
                {
                    BeginBaselineLoad(manager);
                }

                return;
            }

            menuReadyAtRealtime = -1f;
            if (returnToMenuRequested || manager.IsInSceneTransition)
            {
                return;
            }

            returnToMenuRequested = true;
            detail =
                "Returning to the title menu with DontSave before baseline load.";
            var coroutine = manager.ReturnToMainMenu(
                GameManager.ReturnToMainMenuSaveModes.DontSave);
            if (runner == null)
            {
                throw new InvalidOperationException(
                    "Restore runner is unavailable for the menu transition.");
            }

            runner.StartCoroutine(coroutine);
        }

        private void BeginBaselineLoad(GameManager manager)
        {
            if (restoreStrategy == ReplayRestoreStrategy.VanillaEquivalentColdReplay
                && RestoreBaseline.RecordingOrigin != null)
                journal.PrepareInitialRespawn();
            loadCallbackReceived = false;
            loadCallbackSuccess = false;
            consecutiveBaselineMatches = 0;
            phase = ReplayRestorePhase.LoadingBaseline;
            detail =
                "Loading dedicated TAS baseline from the clean title menu.";
            phaseStartedRealtime = Time.realtimeSinceStartup;
            manager.LoadGame(
                dedicatedTasSlot,
                success =>
                {
                    loadCallbackReceived = true;
                    loadCallbackSuccess = success;
                    if (success && GameManager.instance != null)
                    {
                        GameManager.instance.ContinueGame();
                    }
                });
        }

        private void AdvanceLoad()
        {
            if (journal.InitialRespawnPreparationError.Length != 0)
            {
                Fail(ReplaySaveStatus.Failed, journal.InitialRespawnPreparationError);
                return;
            }
            if (loadCallbackReceived && !loadCallbackSuccess)
            {
                Fail(
                    ReplaySaveStatus.Failed,
                    "GameManager rejected the dedicated TAS baseline.");
                return;
            }

            var manager = GameManager.instance;
            var hero = HeroController.SilentInstance;
            if (!loadCallbackReceived
                || !loadCallbackSuccess
                || manager == null
                || manager.profileID != dedicatedTasSlot
                || manager.gameState != GameState.PLAYING
                || manager.IsInSceneTransition
                || hero == null
                || !hero.gameObject.activeInHierarchy
                || InputHandler.Instance?.inputActions == null)
            {
                return;
            }

            // Control ownership belongs to the vanilla load/bench lifecycle.
            // A replay restore may wait for it, but must never manufacture it
            // with RegainControl because that rewrites ActorStates, animation
            // and starting motion outside the recorded input timeline.
            if (!hero.acceptingInput
                && PlayerData.instance?.atBench != true)
            {
                detail =
                    "Waiting for vanilla load lifecycle to restore Hero control.";
                return;
            }

            phase = ReplayRestorePhase.AligningBaseline;
            detail = "Waiting for exact semantic baseline alignment.";
            phaseStartedRealtime = Time.realtimeSinceStartup;
            if (restoreStrategy != ReplayRestoreStrategy.VanillaEquivalentColdReplay)
                AdvanceBaselineAlignment();
        }

        internal void OnCompletedFrame()
        {
            if (disposed
                || phase != ReplayRestorePhase.AligningBaseline
                || restoreStrategy != ReplayRestoreStrategy.VanillaEquivalentColdReplay
                || lastColdBaselineFrame == Time.frameCount)
                return;

            lastColdBaselineFrame = Time.frameCount;
            try
            {
                AdvanceBaselineAlignment();
            }
            catch (Exception exception)
            {
                Fail(ReplaySaveStatus.Failed, exception.GetType().Name + ": " + exception.Message);
            }
        }

        private void AdvanceBaselineAlignment()
        {
            // After the first request, drive the clock handshake at every
            // completed frame, even if a semantic probe temporarily differs.
            // Otherwise a transient mismatch can skip the exact root boundary.
            var coldOriginReady = false;
            if (coldRootRequestSignaled)
            {
                if (!AdvanceColdRecordingRoot()) return;
                coldOriginReady = true;
            }
            var hero = HeroController.SilentInstance;
            if (hero == null || !hero.gameObject.activeInHierarchy)
            {
                consecutiveBaselineMatches = 0;
                return;
            }

            if (!hero.acceptingInput
                && PlayerData.instance?.atBench != true)
            {
                consecutiveBaselineMatches = 0;
                detail =
                    "Waiting for delayed vanilla Hero control lifecycle.";
                return;
            }

            // Schema-2 sources capture their baseline AFTER root preparation.
            // Requiring that post-preparation semantic hash before requesting
            // the target root reverses the source lifecycle and can deadlock.
            var hasPersistedOrigin = restoreStrategy
                == ReplayRestoreStrategy.VanillaEquivalentColdReplay
                && RestoreBaseline.RecordingOrigin != null;
            if (hasPersistedOrigin && !coldOriginReady)
            {
                if (recordingRootCoordinator?.PreparationStarted != true
                    && !journal.CanPrepareRecordingOrigin)
                {
                    detail = "Waiting for the same supported recording anchor as the source.";
                    return;
                }
                if (!AdvanceColdRecordingRoot()) return;
                coldOriginReady = true;
            }

            var capture = CaptureCurrent();
            if (capture.Success
                && string.Equals(
                    capture.Sha256,
                    RestoreBaseline.BaselineSemanticSha256,
                    StringComparison.Ordinal))
            {
                consecutiveBaselineMatches++;
                if (!hasPersistedOrigin && consecutiveBaselineMatches
                    < BaselineStableMatchCount)
                {
                    detail =
                        "Exact semantic baseline candidate observed; "
                        + "waiting across one complete frame for lifecycle stability.";
                    logInfo(
                        "T09 replay restore baseline candidate matched; "
                        + "waiting one complete frame before replay.");
                    return;
                }

                if (hasPersistedOrigin)
                {
                    journal.ReanchorColdBaseline(RestoreBaseline, capture,
                        recordingRootCoordinator!.RecordingBoundaryFrame
                        ?? throw new InvalidOperationException("Cold recording boundary observation is missing."));
                }

                if (!journal.IsAvailable
                    || !string.Equals(
                        journal.BaselineSha256,
                        RestoreBaseline.BaselineSemanticSha256,
                        StringComparison.Ordinal))
                {
                    if (hasPersistedOrigin)
                    {
                        Fail(ReplaySaveStatus.RestoreDesync,
                            "Target shadow journal is not aligned at the persisted recording origin.");
                        return;
                    }
                    detail =
                        "Exact semantic baseline matched; waiting for the "
                        + "same shadow journal baseline to become durable.";
                    if (Time.realtimeSinceStartup - phaseStartedRealtime
                        > BaselineAlignmentTimeoutSeconds)
                    {
                        Fail(
                            ReplaySaveStatus.Failed,
                            "Shadow journal baseline did not become available "
                            + "before prefix replay.");
                    }

                    return;
                }

                if (restoreStrategy
                    == ReplayRestoreStrategy.VanillaEquivalentColdReplay)
                {
                    if (!coldOriginReady && !AdvanceColdRecordingRoot())
                    {
                        return;
                    }

                    EnterColdBaselineBoundary();
                }
                else
                {
                    StartFullReplay();
                }
                return;
            }

            if (coldOriginReady && RestoreBaseline.RecordingOrigin != null)
            {
                var mismatch = capture.Success
                    ? DescribeDifference(
                        SemanticSnapshotCanonicalizer.Deserialize(RestoreBaseline.SemanticSnapshotBytes),
                        capture.Snapshot!)
                    : capture.FailedProbeId + ": " + capture.Error;
                Fail(ReplaySaveStatus.RestoreDesync,
                    "Semantic baseline differs at the persisted recording origin: " + mismatch
                    + "; expectedSha256=" + RestoreBaseline.BaselineSemanticSha256
                    + "; actualSha256=" + capture.Sha256);
                return;
            }

            if (consecutiveBaselineMatches != 0)
            {
                logWarning(
                    "T09 replay restore baseline candidate changed during "
                    + "the following frame; restarting stability alignment.");
                consecutiveBaselineMatches = 0;
            }

            if (Time.realtimeSinceStartup - phaseStartedRealtime
                > (coldRootRequestSignaled
                    ? ColdRecordingRootTimeoutSeconds
                    : BaselineAlignmentTimeoutSeconds))
            {
                var mismatch = capture.Success
                    ? DescribeDifference(
                        SemanticSnapshotCanonicalizer.Deserialize(
                            RestoreBaseline.SemanticSnapshotBytes),
                        capture.Snapshot!)
                    : capture.FailedProbeId + ": " + capture.Error;
                Fail(
                    ReplaySaveStatus.RestoreDesync,
                    "Baseline semantic alignment failed: " + mismatch);
            }
        }

        private bool AdvanceColdRecordingRoot()
        {
            try
            {
                if (recordingRootCoordinator == null)
                {
                    var origin = RestoreBaseline.RecordingOrigin;
                    if (origin != null
                        && !string.Equals(origin.ProfileId, StartupProfileContract.ProfileId, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            "The saved recording origin requires an unsupported startup profile.");
                    recordingRootCoordinator = new RuntimeRecordingRootCoordinator(
                        startupAttestor ?? throw new InvalidOperationException(
                            "Cold restore has no startup profile attestor."),
                        coldIntent?.OperationId ?? throw new InvalidOperationException(
                            "Cold intent is unavailable."),
                        rootBoundarySeconds: origin?.RootBoundarySeconds);
                }
                var ready = recordingRootCoordinator.Advance();
                detail = recordingRootCoordinator.Detail;
                if (ready && RestoreBaseline.RecordingOrigin is RecordingOrigin savedOrigin)
                {
                    var rootFrame = recordingRootCoordinator.RecordingBoundaryFrame
                        ?? throw new InvalidOperationException("Target recording boundary observation is missing.");
                    var elapsed = checked(Time.frameCount - rootFrame);
                    var alignment = savedOrigin.CompareBoundary(elapsed,
                        Time.timeAsDouble, Time.fixedTimeAsDouble);
                    if (alignment == RecordingOriginAlignment.Mismatch)
                        throw new InvalidOperationException(
                            "Target missed or differs from the persisted recording origin: elapsedFrames="
                            + elapsed + ", expected=" + savedOrigin.FramesAfterRootBoundary + ".");
                    if (alignment == RecordingOriginAlignment.Waiting)
                    {
                        detail = "Recording root verified; waiting for persisted journal-origin frame.";
                        return false;
                    }
                }
                return ready;
            }
            catch (Exception exception)
            {
                Fail(ReplaySaveStatus.Failed, exception.Message);
                return false;
            }
        }

        private void EnterColdBaselineBoundary()
        {
            if (coldBoundaryCommandPump == null)
            {
                Fail(
                    ReplaySaveStatus.Failed,
                    "Cold restore has no authenticated boundary command pump.");
                return;
            }

            pauseController = new RuntimePauseController(
                sessionId,
                manifestSha256,
                handle.Id,
                "RESTORE_COLD_BASELINE",
                pausedBoundaryCommandPump: PumpColdRestoreBoundary);
            var pause = pauseController.Pause();
            if (!pause.Success)
            {
                Fail(
                    ReplaySaveStatus.Failed,
                    "Could not enter the cold baseline frame boundary: "
                    + pause.Error);
                return;
            }

            detail =
                "Exact baseline is aligned; entering the completed-frame cold boundary.";
        }

        private void PumpColdRestoreBoundary()
        {
            if (phase == ReplayRestorePhase.ReplayingPrefix && replayLifecycle != null)
            {
                try
                {
                    if (AdvanceReplayLifecycle(true))
                    {
                        coldBoundaryCommandPump?.Invoke();
                        return;
                    }
                    if (currentMovieTick >= EffectiveTargetMovieTick) AdvanceReplay();
                }
                catch (Exception exception)
                {
                    Fail(ReplaySaveStatus.Failed, "Lifecycle replay: " + exception.Message);
                    return;
                }
            }
            if (phase == ReplayRestorePhase.AligningBaseline)
            {
                phase = ReplayRestorePhase.BaselineReady;
                status = ReplaySaveStatus.Restoring;
                detail =
                    "Exact cold baseline is paused at a completed-frame boundary; awaiting authenticated release.";
            }
            else if (phase == ReplayRestorePhase.VerifyingTarget)
            {
                if (!targetHashMatched) CaptureAndVerifyTarget();
                if (phase == ReplayRestorePhase.Failed) return;
                if (!playbackStopped && pauseController?.Mode == SimulationControlMode.Paused
                    && playbackController?.CompleteAtPausedBoundary() != true)
                {
                    Fail(ReplaySaveStatus.Failed,
                        "Completed restore prefix could not release input bindings at the paused target boundary.");
                    return;
                }
                CompleteTargetVerification();
            }

            coldBoundaryCommandPump?.Invoke();
        }

        private void StartFullReplay(bool reusePauseController = false)
        {
            if (!journal.BeginPlaybackCapture(out var captureError))
            {
                Fail(
                    ReplaySaveStatus.Failed,
                    "Replay journal capture could not start before restore "
                    + "prefix: "
                    + captureError);
                return;
            }

            journalPlaybackCaptureActive = true;
            journalPlaybackCaptureError = string.Empty;
            if (!reusePauseController)
            {
                pauseController = new RuntimePauseController(
                    sessionId,
                    manifestSha256,
                    handle.Id,
                    "RESTORE");
            }
            else if (pauseController == null
                     || pauseController.Mode
                        != SimulationControlMode.Running)
            {
                Fail(
                    ReplaySaveStatus.Failed,
                    "Cold baseline pause controller was not released exactly before replay.");
                return;
            }

            replayLifecycle = lifecycleExecutionPlan != null
                ? new ReplayLifecycleCursor(lifecycleExecutionPlan, EffectiveTargetMovieTick)
                : package!.Lifecycle == null ? null
                : new ReplayLifecycleCursor(
                    new ReplayLifecycleLog(package.Descriptor.BaselineObjectSha256,
                        package.Lifecycle.Records.Where(record => record.AfterMovieTick <= EffectiveTargetMovieTick)),
                    targetReplayPrefix!);
            replayLifecycleElapsed = null;
            replayGate = new RestoreMovieTickGate(pauseController,
                () => replayLifecycle == null || replayLifecycle.CanAdvanceInput(currentMovieTick));
            playbackController = new RuntimePlaybackController(
                observation =>
                {
                    currentMovieTick = observation.MovieTick;
                    if (journalPlaybackCaptureActive
                        && !journal.AppendPlaybackObservation(
                            observation,
                            out var appendError))
                    {
                        journalPlaybackCaptureError = appendError;
                        EndJournalPlaybackCapture();
                    }

                    if (!observation.Matches)
                    {
                        detail =
                            "Input adapter mismatch at movie tick "
                            + observation.MovieTick
                            + ".";
                    }
                },
                OnPlaybackEvent,
                (reason, report) =>
                {
                    EndJournalPlaybackCapture();
                    playbackStopped = true;
                    playbackStopReason = reason;
                    bindingRestore = report;
                    heroControlRestoreEquivalent =
                        playbackController?.HeroControlRestoreEquivalent;
                },
                replayGate);
            var start = playbackController.StartReplay(
                targetReplayPrefix
                ?? throw new InvalidOperationException(
                    "Restore replay prefix is unavailable."),
                new PlaybackContext(
                    manifestSha256,
                    RestoreBaseline.BaselineId,
                    RestoreBaseline.BaselineSemanticSha256,
                    playbackController.SceneEpoch,
                    allowSceneTransitions: true));
            if (!start.Success)
            {
                EndJournalPlaybackCapture();
                Fail(
                    ReplaySaveStatus.Failed,
                    "Replay prefix could not start: " + start.Error);
                return;
            }

            currentMovieTick = -1;
            if (replayLifecycle != null && !replayLifecycle.CanAdvanceInput(currentMovieTick))
                playbackController.Replayer!.SetNativeLifecycleSuspended(true);
            phase = ReplayRestorePhase.ReplayingPrefix;
            detail = restoreStrategy
                     == ReplayRestoreStrategy.VanillaEquivalentColdReplay
                ? "Replaying the canonical prefix from the exact cold baseline."
                : "Replaying journal prefix from exact baseline.";
            phaseStartedRealtime = Time.realtimeSinceStartup;
            logInfo(
                "T09 full replay restore started save="
                + RestoreSourceId
                + " targetTick="
                + EffectiveTargetMovieTick);
        }

        private bool AdvanceReplayLifecycle(bool atCompletedBoundary)
        {
            if (pendingLifecycleTermination.HasValue)
                return DrainLifecycleTermination(atCompletedBoundary);
            var cursor = replayLifecycle;
            if (cursor == null || cursor.IsComplete) return false;
            if (cursor.Failure.Length != 0) throw new InvalidOperationException(cursor.Failure);
            if (journal.PlaybackCaptureError.Length != 0)
                throw new InvalidOperationException(journal.PlaybackCaptureError);
            if (cursor.CanAdvanceInput(currentMovieTick)) return false;
            if (restoreStrategy != ReplayRestoreStrategy.VanillaEquivalentColdReplay)
                throw new InvalidOperationException("Lifecycle replay requires completed-frame cold restoration.");

            var active = cursor.Active;
            if (active != null)
            {
                if (active.Kind == ReplayLifecycleKind.LoadSlot && replayNativeCompleted && !replayNativeLoadSucceeded)
                    throw new InvalidOperationException("Native LoadGame reported failure for slot " + active.Slot + ".");
                if (replayLifecycleElapsed!.Elapsed.TotalSeconds > 120)
                    throw new TimeoutException("Native lifecycle replay exceeded 120 seconds.");
                if (!replayNativeCompleted || !journal.ReplayLifecycleDestinationReady) return true;
            }
            if (pauseController!.Mode != SimulationControlMode.Paused)
            {
                var pause = RuntimePauseController.IsStableTitleMenu()
                    ? pauseController.PauseForMenuRestore() : pauseController.Pause();
                if (!pause.Success) throw new InvalidOperationException(pause.Error);
                return true;
            }
            if (!atCompletedBoundary) return true;

            if (active != null)
            {
                lifecycleSlotLease?.Rollback();
                lifecycleSlotLease?.Dispose();
                lifecycleSlotLease = null;
                journal.CompleteReplayLifecycle(cursor);
                replayNativeCommitted = false;
                DetachReplayNativeLoadHook();
                replayLifecycleElapsed = null;
                if (cursor.CanAdvanceInput(currentMovieTick))
                {
                    playbackController!.Replayer!.SetNativeLifecycleSuspended(false);
                    if (currentMovieTick < EffectiveTargetMovieTick)
                    {
                        var resume = pauseController.Resume();
                        if (!resume.Success) throw new InvalidOperationException(resume.Error);
                    }
                    return false;
                }
                // Another operation at this same input boundary is claimed by
                // the next boundary pump, without committing a movie sample.
                return true;
            }

            playbackController!.Replayer!.SetNativeLifecycleSuspended(true);
            var request = cursor.Claim(currentMovieTick, Time.frameCount)
                ?? throw new InvalidOperationException("Lifecycle operation could not be claimed at its boundary.");
            if (request.Kind == ReplayLifecycleKind.LoadSlot)
            {
                var plan = baselineProvider.PlanLifecycleInstall(RestoreBaseline, request, lifecycleExecutionObjects ?? package!.Objects);
                var mayReplaceOwnedBaseline = request.Slot == dedicatedTasSlot && slotLease != null
                    && baselineProvider.PlanInstall(slotLease.Plan.Bundle, dedicatedTasSlot).IsAlreadyInstalled;
                var hasExactConsent = activeLifecycleConsent?.Allows(request.Slot, plan.CurrentSaveData, plan.CurrentModdedSaveData) == true;
                if (activeLifecycleConsent?.Slots.Any(x => x.Slot == request.Slot) == true && !hasExactConsent)
                    throw new InvalidOperationException("Approved lifecycle slot bytes changed; refusing stale consent before any write.");
                if (plan.RequiresOverwriteApproval && !mayReplaceOwnedBaseline && !hasExactConsent)
                    throw new InvalidOperationException("Lifecycle slot requires source-side overwrite approval; current files and backups were preserved.");
                lifecycleSlotLease = CreateRecoverableSlotLease(plan);
                var install = lifecycleSlotLease.Install(mayReplaceOwnedBaseline || hasExactConsent
                    ? UserOverwriteApproval.Approved : UserOverwriteApproval.None);
                if (!install.Success) throw new InvalidOperationException(install.Error);
                if (install.WroteSlot) logInfo("Lifecycle slot backup: " + install.BackupDirectory);
                RuntimeReplayJournal.VerifyLifecycleModdedSlot(request);
                // Verify the installed dat again before invoking native load.
                var slotPath = Path.Combine(Application.persistentDataPath,
                    "user" + request.Slot.ToString(CultureInfo.InvariantCulture) + ".dat");
                var size = new FileInfo(slotPath).Length;
                if (size <= 0 || size > ReplayLifecycleLog.MaximumSlotBytes
                    || Sha256Utility.ComputeFileHex(slotPath) != request.SlotObjectSha256)
                    throw new InvalidDataException("Lifecycle slot object is not installed; refusing changed user slot bytes.");
            }
            journal.BeginReplayLifecycle(cursor, lifecycleExecutionObjects ?? package!.Objects);
            replayLifecycleElapsed = Stopwatch.StartNew();
            var release = pauseController.Resume();
            if (!release.Success) throw new InvalidOperationException(release.Error);
            replayNativeCompleted = false;
            replayNativeStarted = false;
            replayNativeLoadSucceeded = false;
            replayNativeCallbackFrame = 0;
            replayNativeCommitted = true;
            if (request.Kind == ReplayLifecycleKind.ReturnToMenu)
                runner!.StartCoroutine(ObserveNativeMenuReturn(
                    GameManager.instance.ReturnToMainMenu(GameManager.ReturnToMainMenuSaveModes.DontSave)));
            else
            {
                On.GameManager.LoadGame += ObserveReplayNativeLoad;
                replayNativeLoadHookAttached = true;
                var manager = GameManager.instance ?? throw new InvalidOperationException("Game manager is unavailable for native load.");
                replayNativeStarted = true;
                manager.LoadGameFromUI(request.Slot);
            }
            detail = "Replaying native lifecycle operation " + request.Sequence + " (" + request.Kind + ").";
            return true;
        }

        private int slotRecoverySequence;
        private DedicatedTasSlotLease CreateRecoverableSlotLease(BaselineInstallPlan plan)
        {
            var lease = new DedicatedTasSlotLease(baselineProvider, plan);
            if (coldIntent != null)
            {
                using (var process = Process.GetCurrentProcess())
                    lease.ConfigureRecovery(new SlotRecoveryStore(Path.Combine(Application.persistentDataPath,
                        "HollowKnightTAS", "replay-saves", "v1", "slot-recovery")),
                        coldIntent.OperationId, process.Id, new DateTimeOffset(process.StartTime.ToUniversalTime()),
                        checked(slotRecoverySequence++));
            }
            return lease;
        }

        private IEnumerator ObserveNativeMenuReturn(IEnumerator native)
        {
            replayNativeStarted = true;
            yield return native;
            replayNativeCompleted = true;
        }

        private void ObserveReplayNativeLoad(On.GameManager.orig_LoadGame original,
            GameManager self, int slot, Action<bool> callback)
        {
            if (!replayNativeCommitted || replayLifecycle?.Active?.Slot != slot)
            {
                original(self, slot, callback);
                return;
            }
            original(self, slot, success =>
            {
                try { callback?.Invoke(success); }
                finally
                {
                    replayNativeLoadSucceeded = success;
                    replayNativeCallbackFrame = Time.frameCount;
                    replayNativeCompleted = true;
                }
            });
        }

        private void DetachReplayNativeLoadHook()
        {
            if (!replayNativeLoadHookAttached) return;
            On.GameManager.LoadGame -= ObserveReplayNativeLoad;
            replayNativeLoadHookAttached = false;
        }

        private bool DeferLifecycleTermination(ReplaySaveStatus terminalStatus, string reason)
        {
            if (!replayNativeCommitted) return false;
            if (!pendingLifecycleTermination.HasValue)
            {
                pendingLifecycleTermination = terminalStatus;
                pendingLifecycleTerminationReason = reason;
                detail = "Waiting for native lifecycle completion before cleanup: " + reason;
                logWarning(detail);
            }
            return true;
        }

        private bool DrainLifecycleTermination(bool atCompletedBoundary)
        {
            var menu = RuntimePauseController.IsStableTitleMenu();
            var manager = GameManager.instance;
            var gameplay = manager != null && manager.gameState == GameState.PLAYING
                && !manager.IsInSceneTransition && HeroController.SilentInstance?.gameObject.activeInHierarchy == true;
            // A successful LoadGame callback precedes ContinueGame in the UI
            // coroutine. The old title menu is not a completed load boundary.
            var destinationReady = NativeLifecycleTerminationBoundary.CanPause(replayNativeStarted, replayNativeCompleted,
                replayLifecycle?.Active?.Kind == ReplayLifecycleKind.LoadSlot, replayNativeLoadSucceeded,
                replayNativeCallbackFrame, Time.frameCount, menu, gameplay);
            if (!destinationReady) return true;
            if (pauseController!.Mode != SimulationControlMode.Paused)
            {
                var pause = menu ? pauseController.PauseForMenuRestore() : pauseController.Pause();
                if (!pause.Success) throw new InvalidOperationException(pause.Error);
                return true;
            }
            if (!atCompletedBoundary) return true;
            var terminalStatus = pendingLifecycleTermination!.Value;
            var reason = pendingLifecycleTerminationReason;
            pendingLifecycleTermination = null;
            replayNativeCommitted = false;
            DetachReplayNativeLoadHook();
            if (terminalStatus == ReplaySaveStatus.Cancelled) CancelInternal(reason);
            else Fail(terminalStatus, reason);
            if (disposeAfterLifecycle) Dispose();
            return true;
        }

        private void AdvanceReplay()
        {
            if (AdvanceReplayLifecycle(false)) return;
            if (!string.IsNullOrEmpty(journalPlaybackCaptureError))
            {
                Fail(
                    ReplaySaveStatus.RestoreDesync,
                    "Replay journal capture failed during restore prefix: "
                    + journalPlaybackCaptureError);
                return;
            }

            if (playbackStopped
                && playbackStopReason != PlaybackStopReason.Completed
                && !targetCaptured)
            {
                Fail(
                    ReplaySaveStatus.RestoreDesync,
                    "Replay prefix stopped before target: "
                    + playbackStopReason
                    + ".");
                return;
            }

            if (!targetCaptured
                && currentMovieTick
                   >= EffectiveTargetMovieTick)
            {
                var pause = pauseController!.Mode == SimulationControlMode.Paused
                    ? new ControlResult(true, SimulationControlMode.Paused, string.Empty)
                    : pauseController.Pause();
                if (!pause.Success)
                {
                    Fail(
                        ReplaySaveStatus.Failed,
                        "Could not pause at restore target: " + pause.Error);
                    return;
                }

                phase = ReplayRestorePhase.VerifyingTarget;
                detail = "Capturing target semantic hash.";
                targetCaptured = true;
                // Cold capture must observe the same completed-frame phase as
                // source saves, not an earlier LateUpdate render transform.
                if (restoreStrategy != ReplayRestoreStrategy.VanillaEquivalentColdReplay)
                    CaptureAndVerifyTarget();
            }
        }

        private void CaptureAndVerifyTarget()
        {
            var capture = CaptureTargetCurrent();
            targetHashMatched = ApplyTargetVerification(capture);
            if (!targetHashMatched)
            {
                var mismatch = capture.Success && package != null
                    ? DescribeVerificationDifference(package!.TargetSnapshot, capture.Snapshot!)
                    : capture.FailedProbeId + ": " + capture.Error;
                Fail(ReplaySaveStatus.RestoreDesync,
                    "Target semantic SHA-256 verification mismatch "
                    + "(projection " + ReplaySaveSemanticVerifier.ProjectionId
                    + " retained for diagnostics): " + mismatch);
            }
        }

        private void OnPlaybackEvent(
            HollowKnightTAS.Core.Playback.PlaybackEvent playbackEvent)
        {
            if (!(playbackEvent.Command
                  is HollowKnightTAS.Core.Movie.CheckpointCommand checkpoint)
                || !checkpoint.Identifier.StartsWith(
                    ForcedSceneTransitionCommand.Prefix,
                    StringComparison.Ordinal))
            {
                return;
            }

            if (!ForcedSceneTransitionCommand.TryDecode(
                    checkpoint.Identifier,
                    out var spec,
                    out var error)
                || spec == null)
            {
                Fail(
                    ReplaySaveStatus.Corrupt,
                    "Forced scene transition command is invalid: " + error);
                return;
            }

            var manager = GameManager.instance;
            if (manager == null)
            {
                Fail(
                    ReplaySaveStatus.Failed,
                    "GameManager disappeared before forced scene transition.");
                return;
            }

            if (manager.IsInSceneTransition)
            {
                return;
            }

            manager.BeginSceneTransition(spec.ToSceneLoadInfo());
        }

        private void CompleteTargetVerification()
        {
            if (!targetHashMatched)
            {
                return;
            }

            if (playbackStopped
                && (bindingRestore == null
                    || !bindingRestore.Equivalent
                    || heroControlRestoreEquivalent != true))
            {
                Fail(
                    ReplaySaveStatus.Failed,
                    "Input binding or Hero control restoration was not exact.");
                return;
            }

            if (!playbackStopped
                || bindingRestore == null
                || !bindingRestore.Equivalent
                || heroControlRestoreEquivalent != true
                || pauseController?.Mode
                   != SimulationControlMode.Paused)
            {
                return;
            }

            slotLease?.Commit();
            phase = restoreStrategy
                    == ReplayRestoreStrategy.VanillaEquivalentColdReplay
                ? ReplayRestorePhase.PausedAtTarget
                : ReplayRestorePhase.Paused;
            status = ReplaySaveStatus.Ready;
            detail = TargetVerification
                     == ReplayRestoreTargetVerification.ExactSavedSemantic
                ? "Exact target semantic SHA-256 matched; diagnostic projection="
                  + ReplaySaveSemanticVerifier.ProjectionId
                  + "; paused with next movie tick "
                  + checked(EffectiveTargetMovieTick + 1)
                  + "."
                : "New target state was reconstructed only from the persisted canonical movie and observed without claiming a pre-existing semantic hash; paused with next movie tick "
                  + checked(EffectiveTargetMovieTick + 1)
                  + ".";
            logInfo(
                "T09 replay restore verified save="
                + RestoreSourceId
                + " nextTick="
                + checked(EffectiveTargetMovieTick + 1));
        }

        private void BeginAcceleratorVerification()
        {
            pauseController = new RuntimePauseController(
                sessionId,
                manifestSha256,
                handle.Id,
                "RESTORE_ACCELERATED");
            var pause = pauseController.Pause();
            if (!pause.Success)
            {
                logWarning(
                    "T09 accelerator target could not be paused; "
                    + "falling back to clean baseline replay: "
                    + pause.Error);
                PrepareFullReplayFallback();
                return;
            }

            currentMovieTick = package!.Descriptor.EffectiveMovieTick;
            acceleratorVerificationPending = true;
            phase = ReplayRestorePhase.VerifyingTarget;
            detail =
                "Accelerator stopped at the requested cursor; "
                + "capturing target hash at LateUpdate end.";
        }

        private void CompleteAcceleratorVerification()
        {
            acceleratorVerificationPending = false;
            var capture = CaptureTargetCurrent();
            if (!ApplyTargetVerification(capture))
            {
                var mismatch = capture.Success
                    ? DescribeVerificationDifference(
                        package!.TargetSnapshot,
                        capture.Snapshot!)
                    : capture.FailedProbeId + ": " + capture.Error;
                logWarning(
                    "T09 accelerator target verification failed ("
                    + mismatch
                    + "); falling back to clean baseline replay.");
                PrepareFullReplayFallback();
                return;
            }

            currentMovieTick = package!.Descriptor.EffectiveMovieTick;
            targetCaptured = true;
            targetHashMatched = true;
            playbackStopped = true;
            bindingRestore = new BindingRestoreReport(
                false,
                true,
                "No input binding lease was used by the accelerator.",
                Array.Empty<BindingActionSnapshot>(),
                Array.Empty<BindingActionSnapshot>());
            heroControlRestoreEquivalent = true;
            detail =
                "Accelerator restored the exact target semantic SHA-256; "
                + "diagnostic projection="
                + ReplaySaveSemanticVerifier.ProjectionId
                + ".";
            CompleteTargetVerification();
        }

        private void PrepareFullReplayFallback()
        {
            EndJournalPlaybackCapture();
            acceleratorVerificationPending = false;
            targetCaptured = false;
            targetHashMatched = false;
            actualTargetSha256 = string.Empty;
            expectedVerificationSha256 = string.Empty;
            actualVerificationSha256 = string.Empty;
            strictTargetHashMatched = null;
            currentMovieTick = -1;
            playbackStopped = false;
            playbackStopReason = null;
            bindingRestore = null;
            heroControlRestoreEquivalent = null;

            try
            {
                pauseController?.Dispose();
            }
            catch (Exception exception)
            {
                logWarning(
                    "T09 accelerator pause cleanup fault: "
                    + exception.Message);
            }

            pauseController = null;
            if (settingsLease != null)
            {
                if (!settingsLease.Restore())
                {
                    logWarning(
                        "T09 accelerator settings cleanup was not exact "
                        + "before full replay fallback.");
                }

                settingsLease.Dispose();
            }

            settingsLease = new RestoreSettingsLease();
            PlanBaselineInstall();
        }

        private SnapshotCaptureResult CaptureCurrent()
        {
            return snapshotCapture.Capture(
                new TickStamp(
                    InputManager.CurrentTick,
                    visualTick,
                    fixedTick,
                    sceneEpoch,
                    TickPhase.LateUpdateEnd));
        }

        private SnapshotCaptureResult CaptureTargetCurrent()
        {
            return targetSnapshotCapture.Capture(
                new TickStamp(
                    InputManager.CurrentTick,
                    visualTick,
                    fixedTick,
                    sceneEpoch,
                    TickPhase.LateUpdateEnd));
        }

        private bool ApplyTargetVerification(
            SnapshotCaptureResult capture)
        {
            actualTargetSha256 = capture.Sha256 ?? string.Empty;
            if (!capture.Success || capture.Snapshot == null)
            {
                expectedVerificationSha256 = string.Empty;
                actualVerificationSha256 = string.Empty;
                strictTargetHashMatched = null;
                return false;
            }

            if (TargetVerification
                == ReplayRestoreTargetVerification
                    .ReconstructedObservation)
            {
                expectedVerificationSha256 = string.Empty;
                actualVerificationSha256 =
                    ReplaySaveSemanticVerifier
                        .ComputeVerificationSha256(capture.Snapshot);
                strictTargetHashMatched = null;
                return true;
            }

            var comparison = ReplaySaveSemanticVerifier.Compare(
                package!.TargetSnapshot,
                capture.Snapshot);
            expectedVerificationSha256 =
                comparison.ExpectedVerificationSha256;
            actualVerificationSha256 =
                comparison.ActualVerificationSha256;
            strictTargetHashMatched = comparison.StrictEquivalent;
            return comparison.StrictEquivalent;
        }

        private void CancelInternal(string reason)
        {
            if (DeferLifecycleTermination(ReplaySaveStatus.Cancelled, reason)) return;
            cancellation.Cancel();
            CleanupFailedRestore();
            phase = ReplayRestorePhase.Cancelled;
            status = ReplaySaveStatus.Cancelled;
            detail = reason;
            StopRuntimePump();
        }

        private void Fail(ReplaySaveStatus failureStatus, string reason)
        {
            if (phase == ReplayRestorePhase.Failed
                || phase == ReplayRestorePhase.Cancelled)
            {
                return;
            }

            if (DeferLifecycleTermination(failureStatus, reason)) return;

            journal.PersistRestoreFailure(phase.ToString(), reason);
            CleanupFailedRestore();
            phase = ReplayRestorePhase.Failed;
            status = failureStatus;
            detail = reason;
            logError("T09 replay restore failed: " + reason);
            StopRuntimePump();
        }

        private void CleanupFailedRestore()
        {
            DetachReplayNativeLoadHook();
            try
            {
                lifecycleSlotLease?.Rollback();
                lifecycleSlotLease?.Dispose();
                lifecycleSlotLease = null;
            }
            catch (Exception exception)
            {
                logError("Lifecycle slot rollback preserved conflicting files: " + exception.Message);
            }
            try { recordingRootCoordinator?.Dispose(); }
            catch (Exception exception)
            {
                logError("Recording preparation cleanup failed: " + exception.Message);
            }
            try
            {
                playbackController?.Dispose();
            }
            catch (Exception exception)
            {
                logWarning("T09 playback cleanup fault: " + exception.Message);
            }

            playbackController = null;
            EndJournalPlaybackCapture();
            try
            {
                pauseController?.Dispose();
            }
            catch (Exception exception)
            {
                logWarning("T09 pause cleanup fault: " + exception.Message);
            }

            pauseController = null;
            try
            {
                slotLease?.Rollback();
                slotLease?.Dispose();
            }
            catch (Exception exception)
            {
                logError("T09 slot rollback fault: " + exception);
            }

            slotLease = null;
            if (settingsLease != null && !settingsLease.Restore())
            {
                logError(
                    "T09 restore settings cleanup was not exact.");
            }
        }

        private void ResetTerminalState()
        {
            lifecycleSlotLease?.Dispose();
            lifecycleSlotLease = null;
            recordingRootCoordinator?.Dispose();
            playbackController?.Dispose();
            playbackController = null;
            EndJournalPlaybackCapture();
            pauseController?.Dispose();
            pauseController = null;
            slotLease?.Dispose();
            slotLease = null;
            settingsLease?.Dispose();
            settingsLease = null;
            cancellation.Dispose();
            cancellation = new CancellationTokenSource();
            loadTask = null;
            sourceMovieLoadTask = null;
            rootPlanLoadTask = null;
            rootPlanBaseline = null;
            targetMovieLoadTask = null;
            loadResult = null;
            package = null;
            targetMovie = null;
            targetReplayPrefix = null;
            installPlan = null;
            replayGate = null;
            replayLifecycle = null;
            lifecycleExecutionPlan = null;
            activeLifecycleConsent = null;
            lifecycleExecutionObjects = null;
            replayLifecycleElapsed = null;
            replayNativeCommitted = false;
            replayNativeCompleted = false;
            replayNativeStarted = false;
            pendingLifecycleTermination = null;
            pendingLifecycleTerminationReason = string.Empty;
            DetachReplayNativeLoadHook();
            handle = default;
            phase = 0;
            status = 0;
            detail = string.Empty;
            loadCallbackReceived = false;
            loadCallbackSuccess = false;
            playbackStopped = false;
            playbackStopReason = null;
            bindingRestore = null;
            heroControlRestoreEquivalent = null;
            acceleratorVerificationPending = false;
            targetCaptured = false;
            targetHashMatched = false;
            actualTargetSha256 = string.Empty;
            expectedVerificationSha256 = string.Empty;
            actualVerificationSha256 = string.Empty;
            strictTargetHashMatched = null;
            currentMovieTick = -1;
            visualTick = 0;
            fixedTick = 0;
            sceneEpoch = 0;
            menuReadyAtRealtime = -1f;
            returnToMenuRequested = false;
            consecutiveBaselineMatches = 0;
            journalPlaybackCaptureError = string.Empty;
            coldIntent = null;
            recordingRootCoordinator = null;
            lastColdBaselineFrame = -1;
            restoreStrategy =
                ReplayRestoreStrategy.FunctionalReplayRestore;
            equivalenceClass =
                ReplayRestoreEquivalenceClass.FunctionalOnly;
        }

        private void EndJournalPlaybackCapture()
        {
            if (!journalPlaybackCaptureActive)
            {
                return;
            }

            journalPlaybackCaptureActive = false;
            journal.EndPlaybackCapture();
        }

        private void EnsureRuntimePump()
        {
            if (runner != null)
            {
                return;
            }

            var gameObject = new GameObject(
                "HollowKnightTAS.RuntimeReplayRestore");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            runner = gameObject.AddComponent<RuntimeReplayRestoreRunner>();
            runner.Initialize(this);
            ModHooks.BeforeSceneLoadHook += OnBeforeSceneLoad;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged +=
                OnActiveSceneChanged;
        }

        private void StopRuntimePump()
        {
            if (runner == null)
            {
                return;
            }

            ModHooks.BeforeSceneLoadHook -= OnBeforeSceneLoad;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged -=
                OnActiveSceneChanged;
            var current = runner;
            runner = null;
            current.Clear();
            UnityEngine.Object.Destroy(current.gameObject);
        }

        private string OnBeforeSceneLoad(string targetScene)
        {
            return targetScene ?? string.Empty;
        }

        private void OnActiveSceneChanged(
            UnityEngine.SceneManagement.Scene previous,
            UnityEngine.SceneManagement.Scene current)
        {
            sceneEpoch++;
        }

        private bool IsStartedPhase()
        {
            return phase != 0;
        }

        private void ThrowIfWrongHandle(ReplayRestoreHandle value)
        {
            ThrowIfDisposed();
            if (!handle.Equals(value))
            {
                throw new ArgumentException(
                    "Replay restore handle is not active.",
                    nameof(value));
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(RuntimeReplayRestoreCoordinator));
            }
        }

        private static MovieDocument ParseCanonicalMovie(
            byte[] bytes,
            string expectedSha256,
            string sourceName)
        {
            if (!string.Equals(
                    Sha256Utility.ComputeHex(bytes),
                    expectedSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Persisted movie object SHA-256 does not match its intent binding.");
            }

            string source;
            try
            {
                source = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException(
                    "Persisted movie object is not strict UTF-8.",
                    exception);
            }

            MovieParseResult parsed;
            using (var reader = new StringReader(source))
            {
                parsed = new MovieParser().Parse(reader, sourceName);
            }

            if (!parsed.Success || parsed.Document == null)
            {
                throw new InvalidDataException(
                    "Persisted movie object is invalid: "
                    + string.Join(
                        " | ",
                        parsed.Diagnostics
                            .Take(8)
                            .Select(value => value.ToString())));
            }

            var canonical = new MovieCanonicalWriter().WriteUtf8(
                parsed.Document);
            if (!canonical.SequenceEqual(bytes))
            {
                throw new InvalidDataException(
                    "Persisted movie object is not canonical Movie v1 bytes.");
            }

            return parsed.Document;
        }

        private static MovieDocument CreateReplayPrefix(
            MovieDocument movie,
            long targetMovieTick)
        {
            var expandedTicks = CountMovieTicks(movie);
            var prefixTicks = checked(targetMovieTick + 1);
            if (prefixTicks <= 0 || prefixTicks > expandedTicks)
            {
                throw new InvalidDataException(
                    "Cold target tick is outside the persisted movie timeline.");
            }

            return prefixTicks == expandedTicks
                ? movie
                : MovieTimelineEditor.Delete(
                    movie,
                    prefixTicks,
                    expandedTicks - prefixTicks).Movie;
        }

        private static long CountMovieTicks(MovieDocument movie)
        {
            return movie.Commands
                .OfType<FrameRunCommand>()
                .Aggregate(
                    0L,
                    (total, command) => checked(
                        total + command.FrameCount));
        }

        private static string DescribeDifference(
            SemanticSnapshot expected,
            SemanticSnapshot actual)
        {
            var diff = SemanticSnapshotDiffer.Compare(expected, actual);
            if (diff.AreEqual)
            {
                return "hash differed without a semantic field difference";
            }

            var first = diff.FirstDifference!;
            return first.Key
                   + " expected="
                   + first.ExpectedDisplay
                   + " actual="
                   + first.ActualDisplay;
        }

        private static string DescribeVerificationDifference(
            SemanticSnapshot expected,
            SemanticSnapshot actual)
        {
            var projectedExpected =
                ReplaySaveSemanticVerifier.Project(expected);
            var projectedActual =
                ReplaySaveSemanticVerifier.Project(actual);
            var projectedDifference = DescribeDifferences(
                projectedExpected,
                projectedActual);
            var strictDifference = DescribeDifferences(expected, actual);
            return "projected: "
                   + projectedDifference
                   + "; strict-first: "
                   + strictDifference;
        }

        private static string DescribeDifferences(
            SemanticSnapshot expected,
            SemanticSnapshot actual)
        {
            var diff = SemanticSnapshotDiffer.Compare(expected, actual);
            if (diff.AreEqual)
            {
                return "hash differed without a semantic field difference";
            }

            var entries = diff.Entries
                .Take(16)
                .Select(
                    value => value.Key
                             + " expected="
                             + value.ExpectedDisplay
                             + " actual="
                             + value.ActualDisplay);
            return "count="
                   + diff.Entries.Count.ToString(
                       CultureInfo.InvariantCulture)
                   + " ["
                   + string.Join("; ", entries)
                   + "]";
        }

        private static string Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty value is required.",
                    name);
            }

            return value;
        }

        private sealed class RestoreMovieTickGate : IMovieTickGate
        {
            private readonly RuntimePauseController pauseController;
            private readonly Func<bool> lifecycleAllowsInput;

            public RestoreMovieTickGate(
                RuntimePauseController pauseController, Func<bool> lifecycleAllowsInput)
            {
                this.pauseController = pauseController;
                this.lifecycleAllowsInput = lifecycleAllowsInput;
            }

            public bool TryAuthorizeMovieTick(ulong rawInputTick)
            {
                if (!lifecycleAllowsInput()) return false;
                var manager = GameManager.instance;
                var hero = HeroController.SilentInstance;
                if (manager == null
                    || manager.gameState != GameState.PLAYING
                    || manager.IsInSceneTransition
                    || hero == null
                    || !hero.gameObject.activeInHierarchy)
                {
                    return false;
                }

                // Native hitstop (including fractional recovery scales) is
                // part of the input timeline, just as in interactive batches.
                // Canonical scale is required only when establishing a root;
                // filtering it here silently drops frames during boss stagger.
                return pauseController.TryAuthorizeMovieTick(rawInputTick);
            }

            public void OnMovieTickSkipped(ulong rawInputTick)
            {
                pauseController.OnMovieTickSkipped(rawInputTick);
            }

            public void OnMovieTickCommitted(
                long movieTick,
                TickStamp stamp)
            {
                pauseController.OnMovieTickCommitted(movieTick, stamp);
            }
        }
    }

    [DefaultExecutionOrder(-30000)]
    internal sealed class RuntimeReplayRestoreRunner : MonoBehaviour
    {
        private RuntimeReplayRestoreCoordinator? owner;

        internal void Initialize(RuntimeReplayRestoreCoordinator value)
        {
            owner = value;
        }

        internal void Clear()
        {
            owner = null;
        }

        private void OnEnable()
        {
            Application.onBeforeRender += OnBeforeRender;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRender;
        }

        private void OnBeforeRender()
        {
            owner?.OnCompletedFrame();
        }

        private void Update()
        {
            owner?.OnUpdate();
        }

        private void FixedUpdate()
        {
            owner?.OnFixedUpdate();
        }

        private void LateUpdate()
        {
            owner?.OnLateUpdate();
        }
    }
}
