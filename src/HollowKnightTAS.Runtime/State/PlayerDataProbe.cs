using System;
using HollowKnightTAS.Core.State;

namespace HollowKnightTAS.Runtime.State
{
    public sealed class PlayerDataProbe : ISemanticProbe
    {
        public string ProbeId => "player-data";

        public void Capture(SemanticSnapshotBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var playerData = PlayerData.instance;
            if (playerData == null)
            {
                throw new InvalidOperationException(
                    "PlayerData.instance is unavailable.");
            }

            builder.AddInt32("player.health", playerData.health);
            builder.AddInt32("player.maxHealth", playerData.maxHealth);
            builder.AddInt32("player.mp", playerData.MPCharge);
        }
    }
}
