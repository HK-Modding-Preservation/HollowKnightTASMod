using System;
using System.Reflection;
using GlobalEnums;
using HollowKnightTAS.Core.Inspector;

namespace HollowKnightTAS.Runtime.Observation
{
    internal sealed class RuntimeInfoTiming
    {
        private readonly InfoSequenceTimer timer = new InfoSequenceTimer();
        private readonly InfoLoadRemoval removal = new InfoLoadRemoval();
        private bool paused = true;
        public double RealSeconds => timer.RealSeconds;
        public double? GameSeconds => Error.Length == 0 ? timer.GameSeconds : (double?)null;
        public string Error { get; private set; } = "";
        private static readonly FieldInfo? Ui = Field(typeof(GameManager), "_uiInstance");
        private static readonly FieldInfo? Camera = Field(typeof(GameManager), "<cameraCtrl>k__BackingField");
        private static readonly FieldInfo? Input = Field(typeof(GameManager), "<inputHandler>k__BackingField");
        private static readonly FieldInfo? Teleporting = Field(typeof(CameraController), "teleporting");
        private static FieldInfo? Field(Type type, string name) => type.GetField(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public void BeginFrame()
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
                paused = removal.IsPaused(new InfoLoadSample(game, ui != null && ui.uiState == UIState.PLAYING,
                    ui != null && ui.uiState == UIState.PAUSED, manager.sceneName ?? "", manager.nextSceneName ?? "",
                    input != null && input.acceptingInput, camera != null && (bool)Teleporting.GetValue(camera),
                    hero != null && hero.cState.hazardRespawning,
                    hero != null && hero.transitionState == HeroTransitionState.WAITING_TO_ENTER_LEVEL));
            }
            catch (Exception error)
            {
                Error = error.Message; paused = true;
                Modding.Logger.LogWarn("[HKTAS] GT observation unavailable: " + Error);
            }
        }
        public void CompleteFrame(double duration) => timer.CompleteFrame(duration, paused);
    }
}
