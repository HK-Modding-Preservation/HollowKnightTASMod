using System;

namespace HollowKnightTAS.Core.Inspector
{
    public enum InfoGameState { Other, MainMenu, Playing, EnteringLevel, ExitingLevel, Loading }

    public readonly struct InfoLoadSample
    {
        public InfoLoadSample(InfoGameState game, bool uiPlaying, bool uiPaused, string scene, string nextScene,
            bool acceptingInput, bool teleporting, bool hazardRespawning, bool waitingToEnterLevel)
        {
            Game = game; UiPlaying = uiPlaying; UiPaused = uiPaused; Scene = scene; NextScene = nextScene;
            AcceptingInput = acceptingInput; Teleporting = teleporting; HazardRespawning = hazardRespawning;
            WaitingToEnterLevel = waitingToEnterLevel;
        }
        public InfoGameState Game { get; }
        public bool UiPlaying { get; }
        public bool UiPaused { get; }
        public string Scene { get; }
        public string NextScene { get; }
        public bool AcceptingInput { get; }
        public bool Teleporting { get; }
        public bool HazardRespawning { get; }
        public bool WaitingToEnterLevel { get; }
    }

    /// <summary>LiveSplit.HollowKnight load removal for supported HK 1.5, on TAS frame boundaries.
    /// Derived from HollowKnightComponent.LoadRemoval, commit 30084da5385c58e698c4b067fe59e10acd380484.
    /// See third_party/LiveSplit.HollowKnight.LICENSE.txt. No autosplit start/end behavior is implied.</summary>
    public sealed class InfoLoadRemoval
    {
        private InfoGameState previous;
        private bool lookingForTeleport;
        public bool IsPaused(InfoLoadSample sample)
        {
            bool playing = sample.Game == InfoGameState.Playing;
            bool entering = sample.Game == InfoGameState.EnteringLevel;
            if (playing && previous == InfoGameState.MainMenu) lookingForTeleport = true;
            if (lookingForTeleport && (sample.Teleporting || (!playing && !entering))) lookingForTeleport = false;
            previous = sample.Game;
            bool menuLoad = (sample.Scene != "Menu_Title" && string.IsNullOrEmpty(sample.NextScene))
                || (sample.Scene != "Menu_Title" && sample.NextScene == "Menu_Title") || sample.Scene == "Quit_To_Menu";
            return (playing && sample.Teleporting && !sample.HazardRespawning)
                || lookingForTeleport
                || ((playing || entering) && !sample.UiPlaying)
                || (!playing && !sample.AcceptingInput)
                || sample.Game == InfoGameState.ExitingLevel || sample.Game == InfoGameState.Loading
                || sample.WaitingToEnterLevel
                || (!sample.UiPlaying && (menuLoad || (!sample.UiPaused
                    && (!string.IsNullOrEmpty(sample.NextScene) || sample.Scene == "_test_charms")))
                    && sample.NextScene != sample.Scene);
            // LiveSplit's tilemapDirty test is disabled for 1.3+ (UsesSceneTransitionRoutine).
        }
    }

    public sealed class InfoSequenceTimer
    {
        private decimal realSeconds, gameSeconds;
        public double RealSeconds => (double)realSeconds;
        public double GameSeconds => (double)gameSeconds;
        public void CompleteFrame(double duration, bool gameTimePaused)
        {
            if (double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 0)
                throw new ArgumentOutOfRangeException(nameof(duration));
            // Match VariableVideoTimeline: accumulate actual per-frame durations, not frame / current FPS.
            realSeconds += (decimal)duration;
            if (!gameTimePaused) gameSeconds += (decimal)duration;
        }
    }
}
