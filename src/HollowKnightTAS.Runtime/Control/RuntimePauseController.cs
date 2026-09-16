using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using GlobalEnums;
using HollowKnightTAS.Core.Control;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Runtime.Input;
using InControl;
using Modding;
using UnityEngine;
using UnityEngine.SceneManagement;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HollowKnightTAS.Runtime.Control
{
    public sealed class RuntimePauseController : IDisposable, IMovieTickGate
    {
        private const string CompletedFrameBoundaryGateStrategyId =
            "post-render-end-of-frame-boundary-v2";
        private readonly string sessionId;
        private readonly string manifestSha256;
        private readonly string runId;
        private readonly string profile;
        private bool abortOnFocusLoss;
        private bool menuRestorePause;
        private Action? pausedBoundaryCommandPump;
        private readonly Action? completedFrameBoundaryActivation;
        private readonly bool useCompletedFrameBoundaryGate;
        private readonly SimulationControlStateMachine machine =
            new SimulationControlStateMachine();
        private readonly List<TickLedgerRecord> ledger =
            new List<TickLedgerRecord>(4096);
        private readonly Queue<StepResult> completedSteps =
            new Queue<StepResult>();
        private RuntimePauseRunner? runner;
        private TimeScaleGateStrategy? strategy;
        private StepWindow? stepWindow;
        private TimeSettingsSnapshot? pauseBefore;
        private long sequence;
        private long visualTick;
        private long fixedTick;
        private long fixedTickAtPreviousVisual;
        private long totalMovieTicks;
        private ulong currentRawInputTick;
        private int sceneEpoch;
        private bool hooksRegistered;
        private bool disposed;
        private bool heroActionUpdateObserved;
        private bool lastHeroActionUpdateAdvanced;
        private ulong lastHeroActionUpdateTick;
        private bool virtualClockPauseActive;
        private bool shutdownBoundaryReleaseRequested;
        private bool completedFrameSceneTransitionPassThroughActive;

        public RuntimePauseController(
            string sessionId,
            string manifestSha256,
            string runId,
            string profile,
            bool abortOnFocusLoss = true,
            Action? pausedBoundaryCommandPump = null,
            Action? completedFrameBoundaryActivation = null)
        {
            this.sessionId = Require(sessionId, nameof(sessionId));
            this.manifestSha256 = Require(
                manifestSha256,
                nameof(manifestSha256));
            this.runId = Require(runId, nameof(runId));
            this.profile = Require(profile, nameof(profile));
            this.abortOnFocusLoss = abortOnFocusLoss;
            this.pausedBoundaryCommandPump = pausedBoundaryCommandPump;
            this.completedFrameBoundaryActivation =
                completedFrameBoundaryActivation;
            useCompletedFrameBoundaryGate = pausedBoundaryCommandPump != null;
            currentRawInputTick = InputManager.CurrentTick;

            On.InControl.PlayerActionSet.Update +=
                OnPlayerActionSetUpdate;
            InputManager.OnUpdate += OnInputCommitted;
            ModHooks.BeforeSceneLoadHook += OnBeforeSceneLoad;
            USceneManager.activeSceneChanged += OnActiveSceneChanged;
            hooksRegistered = true;

            var gameObject = new GameObject(
                "HollowKnightTAS.RuntimePauseController");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            runner = gameObject.AddComponent<RuntimePauseRunner>();
            runner.Initialize(this);
            var leaseGuard =
                gameObject.AddComponent<RuntimePauseLeaseGuard>();
            leaseGuard.Initialize(this);
        }

        public SimulationControlMode Mode => machine.Mode;
        public string FaultMessage => machine.FaultMessage;
        public long TotalMovieTicks => totalMovieTicks;
        public long VisualTick => visualTick;
        public long FixedTick => fixedTick;
        public ulong CurrentRawInputTick => currentRawInputTick;
        public int SceneEpoch => sceneEpoch;
        public long PauseLeaseReassertionCount { get; private set; }
        public long IgnoredFocusLossCount { get; private set; }
        public long PausedBoundaryWaitCount { get; private set; }
        public long PausedBoundaryPumpCount { get; private set; }
        public long DeferredStepCloseCount { get; private set; }
        public long FrozenHeroActionUpdateCount { get; private set; }
        public long AdvancedHeroActionUpdateCount { get; private set; }
        public long HeroActionPhaseRejectionCount { get; private set; }
        public long VirtualClockPauseCount { get; private set; }
        public long VirtualClockResumeCount { get; private set; }
        public long CompletedFrameSceneTransitionPassThroughCount
        {
            get;
            private set;
        }
        public bool CompletedFrameSceneTransitionPassThroughActive =>
            completedFrameSceneTransitionPassThroughActive;
        public bool HeroActionUpdateObserved => heroActionUpdateObserved;
        public bool LastHeroActionUpdateAdvanced =>
            lastHeroActionUpdateAdvanced;
        public ulong LastHeroActionUpdateTick => lastHeroActionUpdateTick;
        public string LastHeroActionPhaseDiagnostic { get; private set; } =
            string.Empty;
        public TimeSettingsSnapshot? PauseBefore => pauseBefore;
        public TimeSettingsRestoreReport? LastRestoreReport { get; private set; }
        public string LastAbortReason { get; private set; } = string.Empty;
        private string pendingStepInterruption = string.Empty;
        public string LastStepInterruptionReason { get; private set; } = string.Empty;
        public int LastInterruptedStepRequestedTicks { get; private set; }
        public int LastInterruptedStepCommittedTicks { get; private set; }

        internal void InterruptStepAfterCurrentFrame(string reason)
        {
            ThrowIfDisposed();
            if (machine.Mode != SimulationControlMode.Stepping)
                throw new InvalidOperationException("Only an active step can be interrupted.");
            pendingStepInterruption = Require(reason, nameof(reason));
        }
        public string GateStrategyId => useCompletedFrameBoundaryGate
            ? CompletedFrameBoundaryGateStrategyId
            : TimeScaleGateStrategy.StrategyId;
        public bool UsesCompletedFrameBoundaryGate =>
            useCompletedFrameBoundaryGate;
        public IReadOnlyList<TickLedgerRecord> Ledger =>
            new ReadOnlyCollection<TickLedgerRecord>(
                new List<TickLedgerRecord>(ledger));

        public ControlResult Pause()
        {
            return PauseCore(false);
        }

        internal ControlResult PauseForMenuRestore()
        {
            return PauseCore(true);
        }

        internal static bool IsStableTitleMenu()
        {
            return GameManager.instance != null
                && GameManager.instance.gameState == GameState.MAIN_MENU
                && !GameManager.instance.IsInSceneTransition
                && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Menu_Title";
        }

        private ControlResult PauseCore(bool forMenuRestore)
        {
            ThrowIfDisposed();
            if (forMenuRestore ? !IsStableTitleMenu() : GameManager.instance == null
                || GameManager.instance.gameState != GameState.PLAYING)
            {
                return Reject("Pause requires gameplay, or an explicit cold-restore request at the stable title menu.");
            }
            if (useCompletedFrameBoundaryGate
                && !RuntimeVirtualClockBoundary.IsAvailable)
            {
                return Reject(
                    "Frame stepping requires the verified external "
                    + "virtual-clock provider so paused wall time cannot "
                    + "enter Unity unscaled time.");
            }

            var transition = machine.RequestPause();
            if (!transition.Success)
            {
                return transition;
            }

            try
            {
                var lease = new TimeSettingsLease();
                menuRestorePause = forMenuRestore;
                pauseBefore = lease.Before;
                strategy = new TimeScaleGateStrategy(lease);
                if (!useCompletedFrameBoundaryGate)
                {
                    strategy.Close();
                }
                Emit(
                    TickPhase.ProfileApplied,
                    -1,
                    "control=pause;strategy="
                    + GateStrategyId);
                return transition;
            }
            catch (Exception exception)
            {
                Abort(
                    "pause-fault:"
                    + exception.GetType().Name
                    + ":"
                    + exception.Message);
                return Reject(exception.Message);
            }
        }

        public ControlResult Step(StepRequest request)
        {
            ThrowIfDisposed();
            if (request.Boundary != StepBoundary.MovieTick)
            {
                return Reject(
                    "Runtime v1 supports MovieTick steps only; "
                    + "VisualUpdate is observational and is not exposed as a "
                    + "simulation step.");
            }

            if (strategy == null)
            {
                return Reject("Pause time-settings lease is unavailable.");
            }

            var transition = machine.RequestStep(request);
            if (!transition.Success)
            {
                return transition;
            }

            try
            {
                stepWindow = new StepWindow(
                    request,
                    totalMovieTicks,
                    visualTick,
                    fixedTick,
                    currentRawInputTick,
                    ledger.Count);
                if (!useCompletedFrameBoundaryGate)
                {
                    strategy.OpenStepWindow();
                }
                Emit(
                    TickPhase.ProfileApplied,
                    -1,
                    "control=step-open;boundary="
                    + request.Boundary
                    + ";count="
                    + request.Count);
                return transition;
            }
            catch (Exception exception)
            {
                Abort(
                    "step-open-fault:"
                    + exception.GetType().Name
                    + ":"
                    + exception.Message);
                return Reject(exception.Message);
            }
        }

        public ControlResult Resume()
        {
            ThrowIfDisposed();
            var transition = machine.RequestRestore();
            if (!transition.Success)
            {
                return transition;
            }

            var report = RestoreStrategy("resume");
            if (!report.Equivalent)
            {
                return machine.Fault(
                    "Time settings were not restored exactly on resume.");
            }

            var complete = machine.CompleteRestore();
            if (complete.Success)
            {
                menuRestorePause = false;
                completedFrameSceneTransitionPassThroughActive = false;
            }

            return complete;
        }

        internal void ReleaseBoundaryForApplicationQuit()
        {
            ThrowIfDisposed();
            if (machine.Mode != SimulationControlMode.Paused)
                throw new InvalidOperationException("Application quit requires a quiesced paused boundary.");
            // Terminal only: Application.Quit has already been requested.
            // Do not enter Running or grant a gameplay step to leave this loop.
            shutdownBoundaryReleaseRequested = true;
        }

        public void ReplacePausedBoundaryCommandPump(Action replacement)
        {
            ReplacePausedBoundaryCommandPump(replacement, abortOnFocusLoss);
        }

        public void ReplacePausedBoundaryCommandPump(Action replacement, bool abortOnFocusLoss)
        {
            ThrowIfDisposed();
            if (replacement == null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            if (!useCompletedFrameBoundaryGate
                || machine.Mode != SimulationControlMode.Paused)
            {
                throw new InvalidOperationException(
                    "A completed-frame command pump can only be transferred while paused.");
            }

            pausedBoundaryCommandPump = replacement;
            // Transfer the owner's focus policy together with its command pump.
            // No timing lease, input, or gameplay state changes at handoff.
            this.abortOnFocusLoss = abortOnFocusLoss;
        }

        public bool TryTakeCompletedStep(out StepResult? result)
        {
            if (completedSteps.Count == 0)
            {
                result = null;
                return false;
            }

            result = completedSteps.Dequeue();
            return true;
        }

        public bool TryAuthorizeMovieTick(ulong rawInputTick)
        {
            if (disposed)
            {
                return false;
            }

            currentRawInputTick = rawInputTick;
            if (!machine.MovieTickGateOpen)
            {
                return false;
            }

            // InputManager.OnUpdate is downstream of PlayerActionSet.Update.
            // A movie tick is valid only when the vanilla HeroActions set was
            // actually advanced in this same raw InControl tick. This rejects
            // wall-clock-only paused frames without fabricating PlayerAction,
            // Hero, Animator, FSM, Rigidbody2D, or physics state.
            if (!heroActionUpdateObserved
                || lastHeroActionUpdateTick != rawInputTick
                || !lastHeroActionUpdateAdvanced)
            {
                HeroActionPhaseRejectionCount++;
                Emit(
                    TickPhase.ProfileApplied,
                    -1,
                    "control=movie-tick-phase-rejected;rawInputTick="
                    + rawInputTick.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                    + ";lastHeroActionUpdateTick="
                    + lastHeroActionUpdateTick.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                    + ";advanced="
                    + (lastHeroActionUpdateAdvanced ? "true" : "false"));
                return false;
            }

            return true;
        }

        public void OnMovieTickSkipped(ulong rawInputTick)
        {
            currentRawInputTick = rawInputTick;
        }

        public void OnMovieTickCommitted(long movieTick, TickStamp stamp)
        {
            if (disposed)
            {
                return;
            }

            currentRawInputTick = stamp.InputTick;
            totalMovieTicks++;
            if (completedFrameSceneTransitionPassThroughActive)
            {
                completedFrameSceneTransitionPassThroughActive = false;
                Emit(
                    TickPhase.ProfileApplied,
                    -1,
                    "control=scene-transition-pass-through-complete;movieTick="
                    + movieTick.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
            }
            if (machine.Mode != SimulationControlMode.Stepping)
            {
                return;
            }

            var commit = machine.CommitMovieTick();
            if (!commit.Success)
            {
                Abort("step-commit-fault:" + commit.Error);
                return;
            }

            if (!machine.StepQuotaSatisfied)
            {
                return;
            }

            // InputManager.OnUpdate fires immediately after PlayerActionSet
            // commits, before the remainder of the current gameplay frame has
            // consumed that input. Closing Time.timeScale here creates a
            // partial frame: input/VFX can observe the edge while Hero,
            // Animator and the next physics boundary remain paused. Keep the
            // step window open through the complete Update/LateUpdate pipeline;
            // RuntimePauseLeaseGuard closes it at the end-of-frame boundary.
            DeferredStepCloseCount++;
            Emit(
                TickPhase.ProfileApplied,
                -1,
                "control=step-close-deferred;phase=late-update-end;count="
                + DeferredStepCloseCount.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
        }

        public void TriggerControlledFault(string reason)
        {
            ThrowIfDisposed();
            Abort("controlled-fault:" + Require(reason, nameof(reason)));
        }

        public void TriggerFocusLossForProbe()
        {
            ThrowIfDisposed();
            OnApplicationFocus(false);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            if (strategy != null
                || machine.Mode == SimulationControlMode.Pausing
                || machine.Mode == SimulationControlMode.Paused
                || machine.Mode == SimulationControlMode.Stepping
                || machine.Mode == SimulationControlMode.Restoring)
            {
                Abort("mod-shutdown");
            }

            disposed = true;
            UnregisterHooks();
            if (runner != null)
            {
                UnityEngine.Object.Destroy(runner.gameObject);
                runner = null;
            }
        }

        internal void OnUpdate()
        {
            if (disposed)
            {
                return;
            }

            try
            {
                EnforceClosedPauseLease("update-begin");
                visualTick++;
                var fixedSteps = checked(
                    (int)(fixedTick - fixedTickAtPreviousVisual));
                fixedTickAtPreviousVisual = fixedTick;
                Emit(
                    TickPhase.VisualUpdateBegin,
                    fixedSteps,
                    "controlMode=" + machine.Mode);

                if (strategy != null
                    && machine.Mode != SimulationControlMode.Running
                    && machine.Mode != SimulationControlMode.Faulted
                    && GameManager.instance != null
                    && GameManager.instance.gameState != GameState.PLAYING
                    && !(menuRestorePause && IsStableTitleMenu())
                    && !IsCompletedFrameSceneTransitionPassThroughActive())
                {
                    Abort(
                        "game-state-changed:"
                        + GameManager.instance.gameState);
                }
            }
            catch (Exception exception)
            {
                Abort(
                    "update-fault:"
                    + exception.GetType().Name
                    + ":"
                    + exception.Message);
            }
        }

        internal void OnFixedUpdate()
        {
            if (disposed)
            {
                return;
            }

            fixedTick++;
            Emit(
                TickPhase.FixedUpdateBegin,
                -1,
                "controlMode=" + machine.Mode);
        }

        internal void OnLateUpdate()
        {
            if (disposed)
            {
                return;
            }

            try
            {
                Emit(
                    TickPhase.LateUpdateEnd,
                    -1,
                    "controlMode=" + machine.Mode);
                if (machine.Mode == SimulationControlMode.Pausing
                    && pausedBoundaryCommandPump == null)
                {
                    machine.CompletePause();
                    return;
                }

                if (machine.Mode != SimulationControlMode.Stepping
                    || !machine.StepQuotaSatisfied
                    || stepWindow == null)
                {
                    return;
                }

                var window = stepWindow;
                var inputDelta = currentRawInputTick >= window.InputTick
                    ? currentRawInputTick - window.InputTick
                    : throw new InvalidOperationException(
                        "Raw input tick moved backwards during a step.");
                var stepLedger = ledger.GetRange(
                    window.LedgerStartIndex,
                    ledger.Count - window.LedgerStartIndex);
                var result = new StepResult(
                    window.Request,
                    checked((int)(totalMovieTicks - window.MovieTicks)),
                    visualTick - window.VisualTick,
                    fixedTick - window.FixedTick,
                    inputDelta,
                    stepLedger);
                var complete = machine.CompleteStep(result);
                if (!complete.Success)
                {
                    throw new InvalidOperationException(complete.Error);
                }

                stepWindow = null;
                completedSteps.Enqueue(result);
            }
            catch (Exception exception)
            {
                Abort(
                    "late-update-fault:"
                    + exception.GetType().Name
                    + ":"
                    + exception.Message);
            }
        }

        internal void OnEndOfFrameGuard()
        {
            if (disposed)
            {
                return;
            }

            try
            {
                // T24 verification may pre-arm control while the frozen
                // observer waits in LateUpdate on its existing ManualReset
                // event. Consume that event here, inside the same established
                // WaitForEndOfFrame guard, so pause/replay cannot race a
                // second coroutine or release an extra gameplay frame.
                completedFrameBoundaryActivation?.Invoke();
                // Publish the same read-only completed-frame boundary used by
                // the no-Mod observer before this method can block the Unity
                // main thread in the paused command pump. This keeps the last
                // stepped frame observable without releasing an extra frame.
                CompletedFrameBoundarySignal.Publish();
                if (pendingStepInterruption.Length != 0)
                {
                    if (machine.Mode == SimulationControlMode.Stepping && stepWindow != null)
                    {
                        LastStepInterruptionReason = pendingStepInterruption;
                        LastInterruptedStepRequestedTicks = stepWindow.Request.Count;
                        LastInterruptedStepCommittedTicks = machine.CommittedMovieTicks;
                        var interrupted = machine.InterruptStepAtCompletedBoundary();
                        if (!interrupted.Success)
                            throw new InvalidOperationException(interrupted.Error);
                        stepWindow = null;
                        Emit(TickPhase.LateUpdateEnd, -1,
                            "control=step-interrupted;reason=" + Sanitize(LastStepInterruptionReason)
                            + ";committed=" + LastInterruptedStepCommittedTicks
                            + ";requested=" + LastInterruptedStepRequestedTicks);
                    }
                    // A quota already completed before this guard is an ordinary
                    // completion, not a fabricated partial result.
                    pendingStepInterruption = string.Empty;
                }
                if (machine.Mode == SimulationControlMode.Pausing)
                {
                    var complete = machine.CompletePause();
                    if (!complete.Success)
                    {
                        throw new InvalidOperationException(complete.Error);
                    }
                }

                EnforceClosedPauseLease("late-update-end");
                if (pausedBoundaryCommandPump != null
                    && machine.Mode == SimulationControlMode.Paused
                    && !virtualClockPauseActive)
                {
                    RuntimeVirtualClockBoundary.BeginPause();
                    virtualClockPauseActive = true;
                    VirtualClockPauseCount++;
                }
                while (pausedBoundaryCommandPump != null
                       && strategy != null
                       && !shutdownBoundaryReleaseRequested
                       && machine.Mode == SimulationControlMode.Paused)
                {
                    // Companion pause/step never changes Time.timeScale. Block
                    // the Unity main thread only after WaitForEndOfFrame has
                    // resumed, so Unity has completed scripted LateUpdate,
                    // render interpolation and the rendered frame before the
                    // command pump waits. A step releases the completed frame,
                    // the authenticated pipe reader remains on its background
                    // thread and wakes this boundary pump, then blocks again at
                    // the next post-render end-of-frame boundary. This preserves
                    // Rigidbody2D interpolation, Animator, Hero, FSM and
                    // render-transform update semantics inside every released
                    // frame without splitting Unity's PostLateUpdate tail.
                    PausedBoundaryWaitCount++;
                    pausedBoundaryCommandPump();
                    PausedBoundaryPumpCount++;
                    EnforceClosedPauseLease("paused-boundary-pump");
                }
                if (virtualClockPauseActive
                    && (shutdownBoundaryReleaseRequested
                        || machine.Mode != SimulationControlMode.Paused))
                {
                    EndVirtualClockPause();
                }
            }
            catch (Exception exception)
            {
                Abort(
                    "pause-lease-guard-fault:"
                    + exception.GetType().Name
                    + ":"
                    + exception.Message);
            }
        }

        internal void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus
                || disposed
                || machine.Mode == SimulationControlMode.Running
                || machine.Mode == SimulationControlMode.Faulted)
            {
                return;
            }

            if (abortOnFocusLoss)
            {
                Abort("focus-loss");
                return;
            }

            // Companion-driven TAS authoring is intentionally usable while
            // the game is in the background. Losing window focus must not
            // restore the time-settings lease: doing so turns a paused or
            // bounded step into uncontrolled live gameplay. The standalone
            // T08 probe keeps the default abort behavior above, while the
            // Companion controller opts into this fail-closed policy.
            IgnoredFocusLossCount++;
            EnforceClosedPauseLease("focus-loss-ignored");
            Emit(
                TickPhase.ProfileApplied,
                -1,
                "control=focus-loss-ignored;count="
                + IgnoredFocusLossCount.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
        }

        private void OnInputCommitted(ulong inputTick, float deltaTime)
        {
            if (disposed)
            {
                return;
            }

            currentRawInputTick = inputTick;
            Emit(
                TickPhase.InControlCommitted,
                -1,
                "controlMode="
                + machine.Mode
                + ";movieGate="
                + (machine.MovieTickGateOpen ? "open" : "closed"));
        }

        private void OnPlayerActionSetUpdate(
            On.InControl.PlayerActionSet.orig_Update orig,
            PlayerActionSet self,
            ulong updateTick,
            float deltaTime)
        {
            var input = InputHandler.Instance;
            var isHeroActions = input != null
                                && ReferenceEquals(
                                    self,
                                    input.inputActions);
            if (isHeroActions)
            {
                heroActionUpdateObserved = true;
                lastHeroActionUpdateTick = updateTick;
                lastHeroActionUpdateAdvanced = false;
                LastHeroActionPhaseDiagnostic = FormatHeroActionPhase(
                    input!.inputActions,
                    updateTick,
                    "before");
            }

            if (strategy != null
                && isHeroActions
                && !HeroActionsMayAdvance())
            {
                // Unity continues calling Update and InControl while
                // Time.timeScale is zero. Letting the gameplay action set
                // advance in those wall-clock-only frames consumes presses
                // and releases before the requested gameplay step. Preserve
                // the last committed PlayerAction state while paused; the
                // original Update runs exactly once when a complete movie
                // tick window is open.
                FrozenHeroActionUpdateCount++;
                LastHeroActionPhaseDiagnostic = FormatHeroActionPhase(
                    input!.inputActions,
                    updateTick,
                    "frozen");
                return;
            }

            orig(self, updateTick, deltaTime);
            if (isHeroActions)
            {
                lastHeroActionUpdateAdvanced = true;
                AdvancedHeroActionUpdateCount++;
                LastHeroActionPhaseDiagnostic = FormatHeroActionPhase(
                    input!.inputActions,
                    updateTick,
                    "advanced");
            }
        }

        private string FormatHeroActionPhase(
            HeroActions actions,
            ulong updateTick,
            string decision)
        {
            var rightSource = FindTasSource(
                actions.right,
                TasAction.Right);
            return "raw="
                   + updateTick.ToString(
                       System.Globalization.CultureInfo.InvariantCulture)
                   + ",decision="
                   + decision
                   + ",mode="
                   + machine.Mode
                   + ",gate="
                   + (machine.MovieTickGateOpen ? "open" : "closed")
                   + ",setTick="
                   + actions.UpdateTick.ToString(
                       System.Globalization.CultureInfo.InvariantCulture)
                   + ",rightTick="
                   + actions.right.UpdateTick.ToString(
                       System.Globalization.CultureInfo.InvariantCulture)
                   + ",rightBindings="
                   + actions.right.UnfilteredBindings.Count.ToString(
                       System.Globalization.CultureInfo.InvariantCulture)
                   + ",sourceRight="
                   + (rightSource == null
                       ? "missing"
                       : rightSource.CurrentValue.ToString(
                           "R",
                           System.Globalization.CultureInfo
                               .InvariantCulture))
                   + ",rightHeld="
                   + (actions.right.IsPressed ? "true" : "false")
                   + ",rightPressed="
                   + (actions.right.WasPressed ? "true" : "false")
                   + ",rightReleased="
                   + (actions.right.WasReleased ? "true" : "false");
        }

        private static TasBindingSource? FindTasSource(
            PlayerAction action,
            TasAction expectedAction)
        {
            foreach (var binding in action.UnfilteredBindings)
            {
                if (binding is TasBindingSource source
                    && source.Action == expectedAction)
                {
                    return source;
                }
            }

            return null;
        }

        private bool HeroActionsMayAdvance()
        {
            return machine.Mode == SimulationControlMode.Running
                   || machine.Mode == SimulationControlMode.Faulted
                   || machine.Mode == SimulationControlMode.Stepping
                   && machine.MovieTickGateOpen;
        }

        private void OnActiveSceneChanged(Scene previous, Scene current)
        {
            sceneEpoch++;
            Emit(
                TickPhase.ActiveSceneChanged,
                -1,
                "previous="
                + Sanitize(previous.name ?? string.Empty)
                + ";current="
                + Sanitize(current.name ?? string.Empty));
            if (!disposed
                && machine.Mode != SimulationControlMode.Running
                && machine.Mode != SimulationControlMode.Faulted
                && !IsCompletedFrameSceneTransitionPassThroughActive())
            {
                Abort(
                    "scene-change:"
                    + (previous.name ?? string.Empty)
                    + "->"
                    + (current.name ?? string.Empty));
            }
        }

        private string OnBeforeSceneLoad(string targetScene)
        {
            Emit(
                TickPhase.SceneLoadRequested,
                -1,
                "target=" + Sanitize(targetScene ?? string.Empty));
            if (disposed
                || machine.Mode == SimulationControlMode.Running
                || machine.Mode == SimulationControlMode.Faulted)
            {
                return targetScene ?? string.Empty;
            }

            if (CanBeginCompletedFrameSceneTransitionPassThrough())
            {
                if (!completedFrameSceneTransitionPassThroughActive)
                {
                    completedFrameSceneTransitionPassThroughActive = true;
                    CompletedFrameSceneTransitionPassThroughCount++;
                }

                Emit(
                    TickPhase.ProfileApplied,
                    -1,
                    "control=scene-transition-pass-through;target="
                    + Sanitize(targetScene ?? string.Empty)
                    + ";count="
                    + CompletedFrameSceneTransitionPassThroughCount.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                return targetScene ?? string.Empty;
            }

            if (!IsCompletedFrameSceneTransitionPassThroughActive())
            {
                Abort(
                    "scene-load-requested:"
                    + (targetScene ?? string.Empty));
            }

            return targetScene ?? string.Empty;
        }

        private bool CanBeginCompletedFrameSceneTransitionPassThrough()
        {
            var request = machine.ActiveRequest;
            return useCompletedFrameBoundaryGate
                   && machine.Mode == SimulationControlMode.Stepping
                   && request.HasValue
                   && request.Value.Boundary == StepBoundary.MovieTick;
        }

        private bool IsCompletedFrameSceneTransitionPassThroughActive()
        {
            return useCompletedFrameBoundaryGate
                   && completedFrameSceneTransitionPassThroughActive
                   && (machine.Mode == SimulationControlMode.Stepping
                       || machine.Mode == SimulationControlMode.Paused);
        }

        private void EnforceClosedPauseLease(string phase)
        {
            if (useCompletedFrameBoundaryGate
                || strategy == null
                || machine.Mode == SimulationControlMode.Running
                || machine.Mode == SimulationControlMode.Restoring
                || machine.Mode == SimulationControlMode.Faulted
                || machine.Mode == SimulationControlMode.Stepping
                && !machine.StepQuotaSatisfied
                || SingleBits.FromSingle(Time.timeScale)
                == SingleBits.FromSingle(0f))
            {
                return;
            }

            strategy.Close();
            PauseLeaseReassertionCount++;
            Emit(
                TickPhase.ProfileApplied,
                -1,
                "control=pause-reassert;phase="
                + Sanitize(phase)
                + ";count="
                + PauseLeaseReassertionCount.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
        }

        private void Abort(string reason)
        {
            if (disposed)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = "unknown-abort";
            }

            LastAbortReason = reason;
            completedFrameSceneTransitionPassThroughActive = false;
            try
            {
                if (virtualClockPauseActive)
                {
                    EndVirtualClockPause();
                }
                if (machine.Mode != SimulationControlMode.Running
                    && machine.Mode != SimulationControlMode.Restoring)
                {
                    machine.RequestRestore();
                }

                if (strategy != null)
                {
                    var report = RestoreStrategy(reason);
                    if (!report.Equivalent)
                    {
                        reason += ";restore-mismatch";
                    }
                }
            }
            catch (Exception exception)
            {
                reason += ";restore-exception:"
                          + exception.GetType().Name
                          + ":"
                          + exception.Message;
            }

            stepWindow = null;
            machine.Fault(reason);
        }

        private void EndVirtualClockPause()
        {
            RuntimeVirtualClockBoundary.EndPause();
            virtualClockPauseActive = false;
            VirtualClockResumeCount++;
        }

        private TimeSettingsRestoreReport RestoreStrategy(string reason)
        {
            if (strategy == null)
            {
                if (LastRestoreReport != null)
                {
                    return LastRestoreReport;
                }

                throw new InvalidOperationException(
                    "Time-settings strategy is unavailable.");
            }

            var active = strategy;
            var report = active.Restore(reason);
            LastRestoreReport = report;
            Emit(
                TickPhase.ProfileRestored,
                -1,
                "control=restore;reason="
                + Sanitize(reason)
                + ";equivalent="
                + (report.Equivalent ? "true" : "false"));
            active.Dispose();
            strategy = null;
            return report;
        }

        private void Emit(
            TickPhase phase,
            int fixedStepsSincePreviousVisual,
            string detail)
        {
            ledger.Add(
                new TickLedgerRecord(
                    ++sequence,
                    sessionId,
                    manifestSha256,
                    runId,
                    profile,
                    new TickStamp(
                        currentRawInputTick,
                        visualTick,
                        fixedTick,
                        sceneEpoch,
                        phase),
                    fixedStepsSincePreviousVisual,
                    Time.time,
                    Time.fixedTime,
                    Time.time - Time.fixedTime,
                    Time.deltaTime,
                    Time.unscaledDeltaTime,
                    Time.timeScale,
                    Time.realtimeSinceStartup,
                    USceneManager.GetActiveScene().name ?? string.Empty,
                    Sanitize(detail)));
        }

        private void UnregisterHooks()
        {
            if (!hooksRegistered)
            {
                return;
            }

            hooksRegistered = false;
            On.InControl.PlayerActionSet.Update -=
                OnPlayerActionSetUpdate;
            InputManager.OnUpdate -= OnInputCommitted;
            ModHooks.BeforeSceneLoadHook -= OnBeforeSceneLoad;
            USceneManager.activeSceneChanged -= OnActiveSceneChanged;
        }

        private ControlResult Reject(string error)
        {
            return new ControlResult(false, machine.Mode, error);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(RuntimePauseController));
            }
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

        private static string Sanitize(string value)
        {
            return (value ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ');
        }

        private sealed class StepWindow
        {
            public StepWindow(
                StepRequest request,
                long movieTicks,
                long visualTick,
                long fixedTick,
                ulong inputTick,
                int ledgerStartIndex)
            {
                Request = request;
                MovieTicks = movieTicks;
                VisualTick = visualTick;
                FixedTick = fixedTick;
                InputTick = inputTick;
                LedgerStartIndex = ledgerStartIndex;
            }

            public StepRequest Request { get; }
            public long MovieTicks { get; }
            public long VisualTick { get; }
            public long FixedTick { get; }
            public ulong InputTick { get; }
            public int LedgerStartIndex { get; }
        }
    }

    [DefaultExecutionOrder(-32100)]
    internal sealed class RuntimePauseRunner : MonoBehaviour
    {
        private RuntimePauseController? owner;

        internal void Initialize(RuntimePauseController value)
        {
            owner = value;
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

        private void OnApplicationFocus(bool hasFocus)
        {
            owner?.OnApplicationFocus(hasFocus);
        }
    }

    internal sealed class RuntimePauseLeaseGuard : MonoBehaviour
    {
        private RuntimePauseController? owner;
        private readonly WaitForEndOfFrame endOfFrame =
            new WaitForEndOfFrame();

        internal void Initialize(RuntimePauseController value)
        {
            owner = value;
            StartCoroutine(GuardCompletedFrames());
        }

        private IEnumerator GuardCompletedFrames()
        {
            while (true)
            {
                yield return endOfFrame;
                owner?.OnEndOfFrameGuard();
            }
        }
    }
}
