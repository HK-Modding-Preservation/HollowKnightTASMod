using System;
using InControl;

namespace HollowKnightTAS.Runtime.Playback
{
    public sealed class EmergencyStopController : IDisposable
    {
        private readonly EmergencyActionSet actions = new EmergencyActionSet();

        public bool WasPressed => actions.EmergencyStop.WasPressed;

        public void Dispose()
        {
            actions.Destroy();
        }

        private sealed class EmergencyActionSet : PlayerActionSet
        {
            public EmergencyActionSet()
            {
                EmergencyStop = CreatePlayerAction("HKTAS Emergency Stop");
                EmergencyStop.AddDefaultBinding(Key.F8);
            }

            public PlayerAction EmergencyStop { get; }
        }
    }
}
