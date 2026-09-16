using System;
using GlobalEnums;
using HollowKnightTAS.Core.State;
using UnityEngine.SceneManagement;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HollowKnightTAS.Runtime.State
{
    public sealed class SceneProbe : ISemanticProbe
    {
        public string ProbeId => "scene-game";

        public void Capture(SemanticSnapshotBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var scene = USceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.name))
            {
                throw new InvalidOperationException(
                    "The active Unity scene is unavailable.");
            }

            var gameManager = GameManager.instance;
            if (gameManager == null)
            {
                throw new InvalidOperationException(
                    "GameManager.instance is unavailable.");
            }

            var gameState = gameManager.gameState;
            if (!Enum.IsDefined(typeof(GameState), gameState))
            {
                throw new InvalidOperationException(
                    "GameManager.gameState is outside the known enum.");
            }

            builder.AddString("scene.name", scene.name);
            builder.AddString("game.state", gameState.ToString());
        }
    }
}
