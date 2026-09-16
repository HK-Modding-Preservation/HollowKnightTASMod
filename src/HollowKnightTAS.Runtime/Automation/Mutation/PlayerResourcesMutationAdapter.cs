using System;

namespace HollowKnightTAS.Runtime.Automation.Mutation
{
    public sealed class PlayerResourcesMutationAdapter
    {
        public StateMutationApplication Apply(int health, int soul)
        {
            var player = PlayerData.instance
                         ?? throw new InvalidOperationException(
                             "PlayerData is unavailable.");
            var hero = HeroController.SilentInstance
                       ?? throw new InvalidOperationException(
                           "HeroController is unavailable.");
            var maximumHealth = player.CurrentMaxHealth;
            var maximumSoul = player.maxMP;
            if (health < 1 || health > maximumHealth)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(health),
                    "Health must be within current legal bounds.");
            }

            if (soul < 0 || soul > maximumSoul)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(soul),
                    "Soul must be within current legal bounds.");
            }

            var beforeHealth = player.health;
            var beforeSoul = player.MPCharge;
            try
            {
                player.SetInt("health", health);
                hero.SetMPCharge(soul);
            }
            catch
            {
                player.SetInt("health", beforeHealth);
                hero.SetMPCharge(beforeSoul);
                throw;
            }

            return new StateMutationApplication(
                "health="
                + beforeHealth
                + "->"
                + health
                + ";soul="
                + beforeSoul
                + "->"
                + soul,
                () =>
                {
                    player.SetInt("health", beforeHealth);
                    hero.SetMPCharge(beforeSoul);
                });
        }
    }
}
