using System;
using InControl;

namespace HollowKnightTAS.Runtime.Timing
{
    internal sealed class InControlClockProbe : IDisposable
    {
        private readonly Action<ulong, float> onCommitted;
        private bool registered;

        public InControlClockProbe(Action<ulong, float> onCommitted)
        {
            this.onCommitted = onCommitted
                               ?? throw new ArgumentNullException(nameof(onCommitted));
        }

        public ulong CurrentTick { get; private set; }

        public void Start()
        {
            if (registered)
            {
                return;
            }

            registered = true;
            CurrentTick = InputManager.CurrentTick;
            InputManager.OnUpdate += OnInputManagerUpdated;
        }

        public void Dispose()
        {
            if (!registered)
            {
                return;
            }

            registered = false;
            InputManager.OnUpdate -= OnInputManagerUpdated;
        }

        private void OnInputManagerUpdated(ulong inputTick, float deltaTime)
        {
            CurrentTick = inputTick;
            onCommitted(inputTick, deltaTime);
        }
    }
}
