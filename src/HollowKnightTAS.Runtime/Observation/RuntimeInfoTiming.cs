using System;
using System.Reflection;
using System.IO;
using System.Text;
using System.Globalization;
using GlobalEnums;
using HollowKnightTAS.Core.Inspector;

namespace HollowKnightTAS.Runtime.Observation
{
    internal sealed class RuntimeInfoTiming
    {
        private readonly InfoSequenceTimer timer = new InfoSequenceTimer();
        private readonly InfoLoadRemoval removal = new InfoLoadRemoval();
        private bool paused = true;
        private readonly StringBuilder? trace = Environment.GetEnvironmentVariable("HKTAS_INFO_TIMING_TRACE") == "1"
            ? new StringBuilder("movie,native,ready,boundary,game,uiPlaying,uiPaused,scene,nextScene,input,teleport,hazard,waiting,paused,rt,gt\n") : null;
        private string traceSample = "";
        public double RealSeconds => timer.RealSeconds;
        public double? GameSeconds => Error.Length == 0 ? timer.GameSeconds : (double?)null;
        public string Error { get; private set; } = "";
        private static readonly FieldInfo? Ui = Field(typeof(GameManager), "_uiInstance");
        private static readonly FieldInfo? Camera = Field(typeof(GameManager), "<cameraCtrl>k__BackingField");
        private static readonly FieldInfo? Input = Field(typeof(GameManager), "<inputHandler>k__BackingField");
        private static readonly FieldInfo? Teleporting = Field(typeof(CameraController), "teleporting");
        private static FieldInfo? Field(Type type, string name) => type.GetField(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public void BeginFrame(long movieFrame, long nativeFrame, bool ready, string boundary)
        {
            if (Error.Length != 0) return;
            try
            {
                if (Ui == null || Camera == null || Input == null || Teleporting == null)
                    throw new MissingFieldException("LiveSplit timing observation fields are unavailable.");
                var manager = GameManager.instance;
                if (manager == null) { paused = true; return; }
                // Read raw fields only: GameManager.ui's getter may initialize its cached singleton.
                var ui = Ui.GetValue(manager) as UIManager;
                var camera = Camera.GetValue(manager) as CameraController;
                var input = Input.GetValue(manager) as InputHandler;
                var hero = HeroController.SilentInstance;
                var game = manager.gameState switch
                {
                    GameState.MAIN_MENU => InfoGameState.MainMenu, GameState.PLAYING => InfoGameState.Playing,
                    GameState.ENTERING_LEVEL => InfoGameState.EnteringLevel, GameState.EXITING_LEVEL => InfoGameState.ExitingLevel,
                    GameState.LOADING => InfoGameState.Loading, _ => InfoGameState.Other
                };
                var sample = new InfoLoadSample(game, ui != null && ui.uiState == UIState.PLAYING,
                    ui != null && ui.uiState == UIState.PAUSED, manager.sceneName ?? "", manager.nextSceneName ?? "",
                    input != null && input.acceptingInput, camera != null && (bool)Teleporting.GetValue(camera),
                    hero != null && hero.cState.hazardRespawning,
                    hero != null && hero.transitionState == HeroTransitionState.WAITING_TO_ENTER_LEVEL);
                paused = removal.IsPaused(sample);
                if (trace != null && trace.Length < 4 * 1024 * 1024)
                    traceSample = string.Join(",", movieFrame, nativeFrame, ready, boundary, sample.Game,
                        sample.UiPlaying, sample.UiPaused, sample.Scene, sample.NextScene, sample.AcceptingInput,
                        sample.Teleporting, sample.HazardRespawning, sample.WaitingToEnterLevel, paused);
            }
            catch (Exception error)
            {
                Error = error.Message; paused = true;
                Modding.Logger.LogWarn("[HKTAS] GT observation unavailable: " + Error);
            }
        }
        public void CompleteFrame(double duration)
        {
            timer.CompleteFrame(duration, paused);
            if (trace != null && trace.Length < 4 * 1024 * 1024)
                trace.Append(traceSample).Append(',').Append(timer.RealSeconds.ToString("R", CultureInfo.InvariantCulture))
                    .Append(',').Append(timer.GameSeconds.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }
        public void FlushTrace(string directory)
        {
            if (trace == null) return;
            try { File.WriteAllText(Path.Combine(directory, "info-timing.csv"), trace.ToString()); }
            catch (Exception error) { Modding.Logger.LogWarn("[HKTAS] Timing trace unavailable: " + error.Message); }
        }
    }
}
