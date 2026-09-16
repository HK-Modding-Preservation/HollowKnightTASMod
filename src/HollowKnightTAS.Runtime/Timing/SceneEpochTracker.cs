using System;
using Modding;
using UnityEngine.SceneManagement;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HollowKnightTAS.Runtime.Timing
{
    internal sealed class SceneEpochTracker : IDisposable
    {
        private readonly Action<string> onSceneLoadRequested;
        private readonly Action<Scene, Scene, int> onActiveSceneChanged;
        private bool registered;

        public SceneEpochTracker(
            Action<string> onSceneLoadRequested,
            Action<Scene, Scene, int> onActiveSceneChanged)
        {
            this.onSceneLoadRequested = onSceneLoadRequested
                                        ?? throw new ArgumentNullException(
                                            nameof(onSceneLoadRequested));
            this.onActiveSceneChanged = onActiveSceneChanged
                                        ?? throw new ArgumentNullException(
                                            nameof(onActiveSceneChanged));
            CurrentScene = USceneManager.GetActiveScene().name ?? string.Empty;
        }

        public int CurrentEpoch { get; private set; }
        public string CurrentScene { get; private set; }

        public void Start()
        {
            if (registered)
            {
                return;
            }

            registered = true;
            ModHooks.BeforeSceneLoadHook += OnBeforeSceneLoad;
            USceneManager.activeSceneChanged += OnSceneChanged;
        }

        public void ResetForRecording()
        {
            CurrentEpoch = 0;
            CurrentScene = USceneManager.GetActiveScene().name ?? string.Empty;
        }

        public void Dispose()
        {
            if (!registered)
            {
                return;
            }

            registered = false;
            ModHooks.BeforeSceneLoadHook -= OnBeforeSceneLoad;
            USceneManager.activeSceneChanged -= OnSceneChanged;
        }

        private string OnBeforeSceneLoad(string targetScene)
        {
            onSceneLoadRequested(targetScene ?? string.Empty);
            return targetScene ?? string.Empty;
        }

        private void OnSceneChanged(Scene previous, Scene current)
        {
            CurrentEpoch++;
            CurrentScene = current.name ?? string.Empty;
            onActiveSceneChanged(previous, current, CurrentEpoch);
        }
    }
}
