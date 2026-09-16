using System;
using GlobalEnums;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.Playback;
using HollowKnightTAS.Runtime.Timing;
using HollowKnightTAS.Runtime.Control;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HollowKnightTAS.Runtime.Playback
{
    public sealed class RuntimePlaybackController : IDisposable
    {
        private readonly Action<ReplayInputObservation> onObservation;
        private readonly Action<PlaybackEvent> onEvent;
        private readonly Action<PlaybackStopReason, Input.BindingRestoreReport> onStopped;
        private readonly PlaybackStateMachine machine = new PlaybackStateMachine();
        private readonly SceneEpochTracker sceneTracker;
        private readonly EmergencyStopController emergencyStop =
            new EmergencyStopController();
        private readonly IMovieTickGate? movieTickGate;
        private readonly IMovieTickGate replayMovieTickGate;
        private readonly Action<string, string, int>?
            onReplaySceneBoundary;
        private HeroActionReplayer? replayer;
        private RuntimePlaybackRunner? runner;
        private long visualTick;
        private long fixedTick;
        private bool disposed;
        private bool allowSceneTransitions;
        private bool heroControlLeaseActive;
        private bool awaitingVanillaBenchExit;
        private bool pendingHeroControlReacquire;
        private bool sceneTransitionGateClosed;
        private bool frameOpenAtUpdateBegin;
        private string pendingReplayScene = string.Empty;
        private int pendingReplaySceneEpoch = -1;
        private string sceneBoundaryFault = string.Empty;
        private GameManager? subscribedGameManager;

        public RuntimePlaybackController(
            Action<ReplayInputObservation> onObservation,
            Action<PlaybackEvent> onEvent,
            Action<PlaybackStopReason, Input.BindingRestoreReport> onStopped,
            IMovieTickGate? movieTickGate = null,
            Action<string, string, int>? onReplaySceneBoundary = null)
        {
            this.onObservation = onObservation
                                 ?? throw new ArgumentNullException(nameof(onObservation));
            this.onEvent = onEvent ?? throw new ArgumentNullException(nameof(onEvent));
            this.onStopped = onStopped
                             ?? throw new ArgumentNullException(nameof(onStopped));
            this.movieTickGate = movieTickGate;
            this.onReplaySceneBoundary = onReplaySceneBoundary;
            replayMovieTickGate =
                new GameplayAwareMovieTickGate(
                    this.movieTickGate,
                    IsTransitionInputBlocked);
            GameManager.SceneTransitionBegan +=
                OnSceneTransitionBegan;
            EnsureSceneTransitionFinishedSubscription();
            sceneTracker = new SceneEpochTracker(
                OnSceneLoadRequested,
                OnActiveSceneChanged);
            sceneTracker.Start();
            var gameObject = new GameObject("HollowKnightTAS.RuntimePlayback");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            runner = gameObject.AddComponent<RuntimePlaybackRunner>();
            runner.Initialize(this);
        }

        public PlaybackMode Mode => machine.Mode;
        public long ReplayMovieTick =>
            replayer?.LastObservedMovieTick ?? -1;
        public long ReplaySuspendedRawInputTickCount =>
            replayer?.SuspendedRawInputTickCount ?? 0;
        public bool ReplayTimelineActive =>
            replayer != null
            && (machine.Mode == PlaybackMode.Replaying
                || machine.Mode == PlaybackMode.Stopping);
        public int SceneEpoch => sceneTracker.CurrentEpoch;
        public HeroActionReplayer? Replayer => replayer;
        public string FaultMessage =>
            !string.IsNullOrEmpty(sceneBoundaryFault)
                ? sceneBoundaryFault
                : !string.IsNullOrEmpty(replayer?.LastError)
                ? replayer!.LastError
                : machine.FaultMessage;
        public bool? HeroControlRestoreEquivalent { get; private set; }
        public long? FaultInjectionMovieTick { get; set; }

        public PlaybackStartResult StartReplay(
            MovieDocument movie,
            PlaybackContext context)
        {
            ThrowIfDisposed();
            if (context.SceneEpoch != sceneTracker.CurrentEpoch)
            {
                return new PlaybackStartResult(
                    false,
                    "Playback context scene epoch is stale.",
                    null,
                    Array.Empty<PlaybackEvent>());
            }

            var inputHandler = InputHandler.Instance;
            var hero = HeroController.SilentInstance;
            var gameManager = GameManager.instance;
            if (inputHandler == null
                || inputHandler.inputActions == null
                || hero == null
                || !hero.gameObject.activeInHierarchy
                || gameManager == null
                || gameManager.gameState != GameState.PLAYING)
            {
                return new PlaybackStartResult(
                    false,
                    "A playable Hero and HeroActions are required.",
                    null,
                    Array.Empty<PlaybackEvent>());
            }

            sceneTracker.ResetForRecording();
            visualTick = 0;
            fixedTick = 0;
            sceneTransitionGateClosed = false;
            frameOpenAtUpdateBegin = false;
            pendingReplayScene = string.Empty;
            pendingReplaySceneEpoch = -1;
            sceneBoundaryFault = string.Empty;
            try
            {
                AcquireHeroControl(hero);
            }
            catch (Exception exception)
            {
                RestoreHeroControl();
                return new PlaybackStartResult(
                    false,
                    "Hero control could not be acquired: "
                    + exception.Message,
                    null,
                    Array.Empty<PlaybackEvent>());
            }

            var effectiveContext = new PlaybackContext(
                context.ManifestSha256,
                context.BaselineId,
                context.BaselineSha256,
                sceneTracker.CurrentEpoch,
                context.AllowSceneTransitions,
                context.InitialHeld);
            allowSceneTransitions = context.AllowSceneTransitions;
            replayer = new HeroActionReplayer(
                machine,
                CreateStamp,
                onObservation,
                HandleEvent,
                HandleStopped,
                replayMovieTickGate);
            replayer.FaultInjectionMovieTick = FaultInjectionMovieTick;
            var start = replayer.Start(
                inputHandler.inputActions,
                movie,
                effectiveContext);
            if (!start.Success)
            {
                RestoreHeroControl();
            }

            return start;
        }

        public PlaybackStopResult EmergencyStop()
        {
            return RequestStop(PlaybackStopReason.Emergency);
        }

        public PlaybackStopResult Stop(PlaybackStopReason reason)
        {
            return RequestStop(reason);
        }

        internal bool CompleteAtPausedBoundary()
        {
            return !disposed && replayer?.CompleteAtPausedBoundary() == true;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            GameManager.SceneTransitionBegan -=
                OnSceneTransitionBegan;
            if (subscribedGameManager != null)
            {
                subscribedGameManager.OnFinishedSceneTransition -=
                    OnSceneTransitionFinished;
                subscribedGameManager = null;
            }
            replayer?.Dispose();
            replayer = null;
            RestoreHeroControl();
            sceneTracker.Dispose();
            emergencyStop.Dispose();
            if (runner != null)
            {
                UnityEngine.Object.Destroy(runner.gameObject);
                runner = null;
            }
        }

        internal void OnUpdate()
        {
            // Latch scene lifecycle eligibility, not native hitstop state.
            // A zero/partial time scale still runs an input/render frame and
            // advances native unscaled-time coroutines. Dropping those frames
            // makes a paused batch and a continuous replay consume different
            // movie samples around the ramp out of hitstop.
            frameOpenAtUpdateBegin =
                IsVanillaGameplayTickOpenAtUpdateBegin();
            visualTick++;
            EnsureSceneTransitionFinishedSubscription();
            ObserveVanillaBenchExit();
            TryReacquireHeroControlAfterScene();
            if (emergencyStop.WasPressed
                && machine.Mode == PlaybackMode.Replaying)
            {
                EmergencyStop();
            }
        }

        internal void OnFixedUpdate()
        {
            fixedTick++;
        }

        private TickStamp CreateStamp(ulong inputTick)
        {
            return new TickStamp(
                inputTick,
                visualTick,
                fixedTick,
                sceneTracker.CurrentEpoch,
                TickPhase.InControlCommitted);
        }

        private void HandleEvent(PlaybackEvent playbackEvent)
        {
            if (playbackEvent.Command is AssertCommand assertion
                && !RuntimeAssertionEvaluator.Evaluate(assertion))
            {
                RequestStop(PlaybackStopReason.AssertionFailed);
            }

            onEvent(playbackEvent);
        }

        private void HandleStopped(
            PlaybackStopReason reason,
            Input.BindingRestoreReport report)
        {
            RestoreHeroControl();
            onStopped(reason, report);
        }

        private void AcquireHeroControl(HeroController hero)
        {
            heroControlLeaseActive = true;
            awaitingVanillaBenchExit = false;
            pendingHeroControlReacquire = false;
            HeroControlRestoreEquivalent = true;

            // Playback owns only the HeroActions binding source. Whether the
            // Hero currently accepts those actions remains exclusively under
            // the vanilla bench/cutscene/scene-transition lifecycle. Calling
            // RegainControl here rewrites ActorStates and animation/physics
            // state, so a normal TAS path must never do it.
            awaitingVanillaBenchExit =
                !hero.acceptingInput
                && PlayerData.instance?.atBench == true;
        }

        private void RestoreHeroControl()
        {
            if (!heroControlLeaseActive)
            {
                return;
            }

            heroControlLeaseActive = false;
            awaitingVanillaBenchExit = false;
            pendingHeroControlReacquire = false;
            sceneTransitionGateClosed = false;
            // Attaching/removing a binding never owns Hero control. A change in
            // acceptingInput during replay is a vanilla gameplay transition
            // (for example standing up from a bench), and must be preserved.
            HeroControlRestoreEquivalent = true;
        }

        private void ObserveVanillaBenchExit()
        {
            if (!awaitingVanillaBenchExit || !heroControlLeaseActive)
            {
                return;
            }

            var hero = HeroController.SilentInstance;
            if (hero == null || !hero.gameObject.activeInHierarchy)
            {
                return;
            }

            if (!hero.acceptingInput)
            {
                return;
            }

            awaitingVanillaBenchExit = false;
        }

        private void OnActiveSceneChanged(Scene previous, Scene current, int epoch)
        {
            if (machine.Mode == PlaybackMode.Replaying
                && allowSceneTransitions
                && heroControlLeaseActive)
            {
                sceneTransitionGateClosed = true;
                pendingHeroControlReacquire = true;
                pendingReplayScene = current.name ?? string.Empty;
                pendingReplaySceneEpoch = epoch;
            }

            if (machine.Mode == PlaybackMode.Replaying
                && !allowSceneTransitions
                && !string.Equals(
                    previous.name,
                    current.name,
                    StringComparison.Ordinal))
            {
                RequestStop(PlaybackStopReason.SceneChanged);
            }
        }

        private void OnSceneLoadRequested(string targetScene)
        {
            if (machine.Mode != PlaybackMode.Replaying
                || !allowSceneTransitions
                || !heroControlLeaseActive)
            {
                return;
            }

            TryApplyReplaySceneBoundary(
                "transition-start",
                targetScene,
                checked(sceneTracker.CurrentEpoch + 1));
        }

        private void TryReacquireHeroControlAfterScene()
        {
            if (!pendingHeroControlReacquire
                || !heroControlLeaseActive
                || !allowSceneTransitions)
            {
                return;
            }

            var manager = GameManager.instance;
            var hero = HeroController.SilentInstance;
            if (manager == null
                || manager.gameState != GameState.PLAYING
                || manager.IsInSceneTransition
                || hero == null
                || !hero.gameObject.activeInHierarchy)
            {
                return;
            }

            // T19 freezes only loading/transition boundaries. Vanilla
            // no_input states after a load (for example Godhome Prostrate)
            // must keep consuming movie samples so an explicit release can
            // reach the game; acceptingInput is intentionally not a gate.
            pendingHeroControlReacquire = false;
            sceneTransitionGateClosed = false;
        }

        private void EnsureSceneTransitionFinishedSubscription()
        {
            var manager = GameManager.instance;
            if (ReferenceEquals(manager, subscribedGameManager))
            {
                return;
            }

            if (subscribedGameManager != null)
            {
                subscribedGameManager.OnFinishedSceneTransition -=
                    OnSceneTransitionFinished;
            }

            subscribedGameManager = manager;
            if (subscribedGameManager != null)
            {
                subscribedGameManager.OnFinishedSceneTransition +=
                    OnSceneTransitionFinished;
            }
        }

        private void OnSceneTransitionBegan(SceneLoad sceneLoad)
        {
            if (machine.Mode == PlaybackMode.Replaying
                && allowSceneTransitions
                && heroControlLeaseActive)
            {
                sceneTransitionGateClosed = true;
            }
        }

        private void OnSceneTransitionFinished()
        {
            sceneTransitionGateClosed = false;
            if (machine.Mode == PlaybackMode.Replaying
                && allowSceneTransitions
                && heroControlLeaseActive)
            {
                pendingHeroControlReacquire = true;
            }
        }

        private bool IsTransitionInputBlocked(ulong rawInputTick)
        {
            if (!frameOpenAtUpdateBegin)
            {
                return true;
            }

            if (sceneTransitionGateClosed
                || pendingHeroControlReacquire)
            {
                return true;
            }

            if (pendingReplaySceneEpoch >= 0)
            {
                if (!TryApplyReplaySceneBoundary(
                        "gameplay-ready",
                        pendingReplayScene,
                        pendingReplaySceneEpoch))
                {
                    return true;
                }

                pendingReplayScene = string.Empty;
                pendingReplaySceneEpoch = -1;
            }

            return false;
        }

        private static bool IsVanillaGameplayTickOpenAtUpdateBegin()
        {
            var manager = GameManager.instance;
            var hero = HeroController.SilentInstance;
            var input = InputHandler.Instance;
            var scene = UnityEngine.SceneManagement.SceneManager
                .GetActiveScene();

            if (manager == null
                || manager.gameState != GameState.PLAYING
                || manager.IsInSceneTransition
                || hero == null
                || !hero.gameObject.activeInHierarchy
                || input == null
                || input.inputActions == null
                || !scene.IsValid()
                || !scene.isLoaded
                || string.IsNullOrEmpty(scene.name)
                || hero.cState.transitioning)
            {
                return false;
            }

            if (!hero.acceptingInput)
            {
                var animation =
                    hero.GetComponent<HeroAnimationController>();
                if (string.Equals(
                        animation?.animator?.CurrentClip?.name,
                        "Challenge Start",
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryApplyReplaySceneBoundary(
            string boundary,
            string sceneName,
            int sceneEpoch)
        {
            if (onReplaySceneBoundary == null)
            {
                return true;
            }

            try
            {
                onReplaySceneBoundary(
                    boundary,
                    sceneName ?? string.Empty,
                    sceneEpoch);
                return true;
            }
            catch (Exception exception)
            {
                sceneBoundaryFault =
                    "Replay scene RNG boundary failed: "
                    + exception.GetType().Name
                    + ": "
                    + exception.Message;
                RequestStop(PlaybackStopReason.RuntimeFault);
                return false;
            }
        }

        private PlaybackStopResult RequestStop(PlaybackStopReason reason)
        {
            ThrowIfDisposed();
            if (replayer == null)
            {
                return new PlaybackStopResult(
                    false,
                    reason,
                    default,
                    "No replayer is active.");
            }

            return replayer.RequestStop(reason);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(RuntimePlaybackController));
            }
        }
    }

    internal sealed class GameplayAwareMovieTickGate :
        IMovieTickGate,
        ITransitionReleaseMovieTickGate
    {
        private readonly IMovieTickGate? inner;
        private readonly Func<ulong, bool> transitionBlocked;
        private ulong lastRejectedRawInputTick;
        private bool lastRejectionWasGameplayTransition;

        public GameplayAwareMovieTickGate(
            IMovieTickGate? inner,
            Func<ulong, bool> transitionBlocked)
        {
            this.inner = inner;
            this.transitionBlocked = transitionBlocked
                                     ?? throw new ArgumentNullException(
                                         nameof(transitionBlocked));
        }

        public bool TryAuthorizeMovieTick(ulong rawInputTick)
        {
            lastRejectedRawInputTick = rawInputTick;
            lastRejectionWasGameplayTransition = false;
            var manager = GameManager.instance;
            var hero = HeroController.SilentInstance;
            var input = InputHandler.Instance;
            var scene = UnityEngine.SceneManagement.SceneManager
                .GetActiveScene();
            if (transitionBlocked(rawInputTick)
                || manager == null
                || manager.gameState != GameState.PLAYING
                || manager.IsInSceneTransition
                || hero == null
                || !hero.gameObject.activeInHierarchy
                || input == null
                || input.inputActions == null
                || !scene.IsValid()
                || !scene.isLoaded
                || string.IsNullOrEmpty(scene.name))
            {
                lastRejectionWasGameplayTransition = true;
                return false;
            }

            if (hero.cState.transitioning
                || IsGodhomeChallengeTransition(hero))
            {
                lastRejectionWasGameplayTransition = true;
                return false;
            }

            return inner?.TryAuthorizeMovieTick(rawInputTick) ?? true;
        }

        public bool TryAuthorizeTransitionRelease(ulong rawInputTick)
        {
            // A release may cross a vanilla scene-transition suspension, but
            // it must never bypass the lower pause/step gate. In particular,
            // a release prepared after a completed single step remains
            // pending throughout wall-clock-only paused frames.
            return lastRejectedRawInputTick == rawInputTick
                   && lastRejectionWasGameplayTransition
                   && (inner?.TryAuthorizeMovieTick(rawInputTick) ?? true);
        }

        private static bool IsGodhomeChallengeTransition(
            HeroController hero)
        {
            if (hero.acceptingInput)
            {
                return false;
            }

            var animation =
                hero.GetComponent<HeroAnimationController>();
            var clip = animation?.animator?.CurrentClip;
            return string.Equals(
                clip?.name,
                "Challenge Start",
                StringComparison.Ordinal);
        }

        public void OnMovieTickSkipped(ulong rawInputTick)
        {
            inner?.OnMovieTickSkipped(rawInputTick);
        }

        public void OnMovieTickCommitted(
            long movieTick,
            TickStamp stamp)
        {
            inner?.OnMovieTickCommitted(movieTick, stamp);
        }
    }

    [DefaultExecutionOrder(-32000)]
    internal sealed class RuntimePlaybackRunner : MonoBehaviour
    {
        private RuntimePlaybackController? owner;

        internal void Initialize(RuntimePlaybackController value)
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
    }

    internal static class RuntimeAssertionEvaluator
    {
        public static bool Evaluate(AssertCommand assertion)
        {
            if (assertion == null)
            {
                throw new ArgumentNullException(nameof(assertion));
            }

            if (string.Equals(
                    assertion.SemanticPath,
                    "scene.name",
                    StringComparison.Ordinal)
                && string.Equals(
                    assertion.Operator,
                    "==",
                    StringComparison.Ordinal)
                && assertion.ValueIsQuoted)
            {
                return string.Equals(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                    assertion.Value,
                    StringComparison.Ordinal);
            }

            return false;
        }
    }
}
