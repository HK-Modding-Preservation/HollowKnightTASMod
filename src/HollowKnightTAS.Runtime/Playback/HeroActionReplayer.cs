using System;
using System.Collections.Generic;
using System.Linq;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.Playback;
using HollowKnightTAS.Runtime.Input;
using HollowKnightTAS.Runtime.Control;
using InControl;

namespace HollowKnightTAS.Runtime.Playback
{
    public sealed class ReplayInputObservation
    {
        internal ReplayInputObservation(
            long movieTick,
            ulong rawInputTick,
            InputSample expected,
            CapturedHeroInput actual,
            bool controlSignalMatches,
            bool committedEdgesMatch,
            bool physicalNoise,
            bool isReleaseBoundary,
            bool attachedActionSetIsCurrent)
        {
            MovieTick = movieTick;
            RawInputTick = rawInputTick;
            Expected = expected;
            Actual = actual;
            Matches = controlSignalMatches;
            ControlSignalMatches = controlSignalMatches;
            CommittedEdgesMatch = committedEdgesMatch;
            PhysicalNoise = physicalNoise;
            IsReleaseBoundary = isReleaseBoundary;
            AttachedActionSetIsCurrent = attachedActionSetIsCurrent;
        }

        public long MovieTick { get; }
        public ulong RawInputTick { get; }
        public InputSample Expected { get; }
        public CapturedHeroInput Actual { get; }
        public bool Matches { get; }
        public bool ControlSignalMatches { get; }
        public bool CommittedEdgesMatch { get; }
        public bool PhysicalNoise { get; }
        public bool IsReleaseBoundary { get; }
        public bool AttachedActionSetIsCurrent { get; }
    }

    public sealed class HeroActionReplayer : IDisposable
    {
        private readonly PlaybackStateMachine machine;
        private readonly Func<ulong, TickStamp> stampFactory;
        private readonly Action<ReplayInputObservation> onObservation;
        private readonly Action<PlaybackEvent> onEvent;
        private readonly Action<PlaybackStopReason, BindingRestoreReport> onStopped;
        private readonly IMovieTickGate? movieTickGate;
        private readonly HeroInputAdapter adapter = new HeroInputAdapter();
        private InputSample preparedInput;
        private bool registered;
        private bool firstInputPrimingRegistered;
        private bool firstInputPending;
        private bool attached;
        private bool releasePending;
        private bool stopped;
        private bool nativeLifecycleSuspended;
        private HeroActions? attachedActions;
        private long consumedMovieTicks;

        public HeroActionReplayer(
            PlaybackStateMachine machine,
            Func<ulong, TickStamp> stampFactory,
            Action<ReplayInputObservation> onObservation,
            Action<PlaybackEvent> onEvent,
            Action<PlaybackStopReason, BindingRestoreReport> onStopped,
            IMovieTickGate? movieTickGate = null)
        {
            this.machine = machine
                           ?? throw new ArgumentNullException(nameof(machine));
            this.stampFactory = stampFactory
                                ?? throw new ArgumentNullException(nameof(stampFactory));
            this.onObservation = onObservation
                                 ?? throw new ArgumentNullException(nameof(onObservation));
            this.onEvent = onEvent ?? throw new ArgumentNullException(nameof(onEvent));
            this.onStopped = onStopped
                             ?? throw new ArgumentNullException(nameof(onStopped));
            this.movieTickGate = movieTickGate;
        }

        public bool PhysicalNoiseDetected { get; private set; }
        public long ObservationCount { get; private set; }
        public long SuspendedRawInputTickCount { get; private set; }
        public long LastObservedMovieTick { get; private set; } = -1;
        public long MismatchCount { get; private set; }
        public BindingRestoreReport? RestoreReport { get; private set; }
        public IReadOnlyList<BindingActionSnapshot> BindingBefore =>
            adapter.BindingBefore;
        public string LastError { get; private set; } = string.Empty;
        public long? FaultInjectionMovieTick { get; set; }

        public PlaybackStartResult Start(
            HeroActions actions,
            MovieDocument movie,
            PlaybackContext context)
        {
            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions));
            }

            var start = machine.StartReplay(movie, context);
            if (!start.Success)
            {
                return start;
            }

            try
            {
                LastError = string.Empty;
                adapter.Attach(actions);
                attached = true;
                attachedActions = actions;
                preparedInput = start.FirstInput!.Value;
                adapter.Prepare(
                    InputSample.FromHeld(
                        preparedInput.InputTick,
                        TasAction.None,
                        TasAction.None));
                firstInputPending = true;
                On.InControl.PlayerActionSet.Update +=
                    OnPlayerActionSetUpdating;
                firstInputPrimingRegistered = true;
                foreach (var playbackEvent in start.Events)
                {
                    onEvent(playbackEvent);
                }

                InputManager.OnUpdate += OnInputManagerUpdated;
                registered = true;
                return start;
            }
            catch (Exception exception)
            {
                machine.Stop(PlaybackStopReason.AdapterFault);
                Cleanup(
                    PlaybackStopReason.AdapterFault,
                    exception.GetType().Name + ": " + exception.Message);
                return new PlaybackStartResult(
                    false,
                    exception.Message,
                    null,
                    Array.Empty<PlaybackEvent>());
            }
        }

        public PlaybackStopResult RequestStop(PlaybackStopReason reason)
        {
            if (stopped)
            {
                return new PlaybackStopResult(
                    false,
                    reason,
                    preparedInput,
                    "Replayer is already stopped.");
            }

            var result = machine.Stop(reason);
            try
            {
                preparedInput = result.ReleaseInput;
                adapter.Prepare(preparedInput);
                releasePending = true;
                return result;
            }
            catch (Exception exception)
            {
                Cleanup(
                    PlaybackStopReason.AdapterFault,
                    exception.GetType().Name + ": " + exception.Message);
                return new PlaybackStopResult(
                    false,
                    PlaybackStopReason.AdapterFault,
                    preparedInput,
                    exception.Message);
            }
        }

        public void Dispose()
        {
            if (!stopped)
            {
                Cleanup(
                    machine.StopReason ?? PlaybackStopReason.RuntimeFault,
                    string.Empty);
            }
        }

        private void OnInputManagerUpdated(ulong inputTick, float deltaTime)
        {
            if (stopped || !attached)
            {
                return;
            }

            try
            {
                if (nativeLifecycleSuspended)
                {
                    machine.DeclareRawInputSuspension();
                    SuspendedRawInputTickCount++;
                    movieTickGate?.OnMovieTickSkipped(inputTick);
                    return;
                }
                var stamp = stampFactory(inputTick);
                if (!releasePending
                    && movieTickGate != null
                    && !movieTickGate.TryAuthorizeMovieTick(inputTick))
                {
                    // A prepared release may already be visible to vanilla
                    // input, but it is not a completed gameplay movie frame.
                    // Counting it during loading makes a restore stop in the
                    // old scene while native continuation counts after loading.
                    machine.DeclareRawInputSuspension();
                    SuspendedRawInputTickCount++;
                    movieTickGate.OnMovieTickSkipped(inputTick);
                    return;
                }

                var actual = adapter.Observe(inputTick);
                // The movie is a raw control signal: binding values/held state
                // and axes are authoritative. WasPressed/WasReleased are
                // committed by the original PlayerAction update and can be
                // intentionally suppressed when Hollow Knight resets or gates
                // an action set (for example during damage stun). Treat those
                // edges as an observed vanilla result, never as a value the
                // TAS layer is allowed to force or as an adapter fault.
                var matches = actual.MatchesControlSignal(preparedInput);
                var committedEdgesMatch =
                    actual.CommittedEdgesMatch(preparedInput);
                var physicalNoise = adapter
                    .ObservePhysical()
                    .Any(value => value.Value >= 0.5f);
                PhysicalNoiseDetected |= physicalNoise;
                ObservationCount++;
                if (!matches)
                {
                    MismatchCount++;
                }

                var isReleaseBoundary = releasePending;
                var observedMovieTick = isReleaseBoundary
                    ? consumedMovieTicks
                    : consumedMovieTicks++;
                LastObservedMovieTick = observedMovieTick;
                onObservation(
                    new ReplayInputObservation(
                        observedMovieTick,
                        inputTick,
                        preparedInput,
                        actual,
                        matches,
                        committedEdgesMatch,
                        physicalNoise,
                        isReleaseBoundary,
                        ReferenceEquals(
                            attachedActions,
                            InputHandler.Instance?.inputActions)));

                if (!matches)
                {
                    RequestStop(PlaybackStopReason.AdapterFault);
                    return;
                }

                CompleteFirstInputPriming();

                if (FaultInjectionMovieTick.HasValue
                    && consumedMovieTicks - 1 == FaultInjectionMovieTick.Value)
                {
                    FaultInjectionMovieTick = null;
                    RequestStop(PlaybackStopReason.AdapterFault);
                    return;
                }

                if (releasePending)
                {
                    Cleanup(
                        machine.StopReason ?? PlaybackStopReason.Completed,
                        string.Empty);
                    return;
                }

                var tickResult = machine.Advance(stamp);
                foreach (var playbackEvent in tickResult.Events)
                {
                    onEvent(playbackEvent);
                }

                if (!tickResult.Success)
                {
                    preparedInput = tickResult.NextInput;
                    adapter.Prepare(preparedInput);
                    releasePending = true;
                    return;
                }

                movieTickGate?.OnMovieTickCommitted(
                    observedMovieTick,
                    stamp);

                preparedInput = tickResult.NextInput;
                adapter.Prepare(preparedInput);
                releasePending = tickResult.ReleaseBoundary;
            }
            catch (Exception exception)
            {
                Cleanup(
                    PlaybackStopReason.AdapterFault,
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal bool CompleteAtPausedBoundary()
        {
            // The last movie sample has completed its entire Unity frame.
            // A synthetic release update would add a frame beyond a restore
            // target. Restore bindings directly, without running input/gameplay.
            if (stopped || !releasePending || machine.Mode != PlaybackMode.Stopping
                || (machine.StopReason != PlaybackStopReason.Completed
                    && machine.StopReason != PlaybackStopReason.Manual))
                return false;
            Cleanup(machine.StopReason.Value, string.Empty);
            return true;
        }

        internal void SetNativeLifecycleSuspended(bool suspended)
        {
            if (stopped || !attached)
                throw new InvalidOperationException("Native lifecycle requires an attached replay input owner.");
            nativeLifecycleSuspended = suspended;
            adapter.Prepare(suspended
                ? InputSample.FromHeld(preparedInput.InputTick, TasAction.None, TasAction.None)
                : preparedInput);
            if (suspended && !firstInputPrimingRegistered)
            {
                On.InControl.PlayerActionSet.Update += OnPlayerActionSetUpdating;
                firstInputPrimingRegistered = true;
            }
            else if (!suspended && !firstInputPending) CompleteFirstInputPriming();
        }

        private void Cleanup(PlaybackStopReason reason, string error)
        {
            if (stopped)
            {
                return;
            }

            stopped = true;
            nativeLifecycleSuspended = false;
            LastError = error ?? string.Empty;
            CompleteFirstInputPriming();
            if (registered)
            {
                InputManager.OnUpdate -= OnInputManagerUpdated;
                registered = false;
            }

            try
            {
                if (attached)
                {
                    adapter.Prepare(
                        InputSample.FromHeld(
                            preparedInput.InputTick,
                            TasAction.None,
                            TasAction.None));
                }
            }
            catch
            {
                // Binding restoration below is still mandatory.
            }

            RestoreReport = adapter.DetachAndRestore();
            attached = false;
            attachedActions = null;
            machine.CompleteCleanup(
                RestoreReport.Equivalent,
                LastError);
            onStopped(reason, RestoreReport);
        }

        private void OnPlayerActionSetUpdating(
            On.InControl.PlayerActionSet.orig_Update original,
            PlayerActionSet self,
            ulong updateTick,
            float deltaTime)
        {
            try
            {
                if (!stopped && nativeLifecycleSuspended && self is HeroActions actions
                    && ReferenceEquals(actions, InputHandler.Instance?.inputActions))
                {
                    if (!ReferenceEquals(actions, attachedActions))
                    {
                        var report = adapter.DetachAndRestore();
                        attached = false;
                        if (!report.Equivalent) throw new InvalidOperationException(report.Message);
                        adapter.Attach(actions);
                        attached = true;
                        attachedActions = actions;
                    }
                    adapter.Prepare(InputSample.FromHeld(preparedInput.InputTick, TasAction.None, TasAction.None));
                }
                else if (!stopped
                    && attached
                    && firstInputPending
                    && ReferenceEquals(self, attachedActions))
                {
                    // startReplay can arrive at any point in a Unity frame.
                    // Keep the replacement bindings neutral until the exact
                    // vanilla HeroActions update that will commit the first
                    // sample. The ordinary PlayerAction edge calculation then
                    // produces WasPressed/WasReleased for gameplay consumers;
                    // no Hero/FSM/Animator/physics state is fabricated here.
                    adapter.Prepare(preparedInput);
                }
            }
            catch (Exception exception)
            {
                Cleanup(
                    PlaybackStopReason.AdapterFault,
                    exception.GetType().Name + ": " + exception.Message);
            }
            finally
            {
                original(self, updateTick, deltaTime);
            }
        }

        private void CompleteFirstInputPriming()
        {
            firstInputPending = false;
            if (!firstInputPrimingRegistered)
            {
                return;
            }

            On.InControl.PlayerActionSet.Update -=
                OnPlayerActionSetUpdating;
            firstInputPrimingRegistered = false;
        }

    }
}
