using System;
using GlobalEnums;
using HollowKnightTAS.Core.State;
using UnityEngine;

namespace HollowKnightTAS.Runtime.State
{
    public sealed class HeroProbe : ISemanticProbe
    {
        public string ProbeId => "hero";

        public void Capture(SemanticSnapshotBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var hero = HeroController.SilentInstance;
            if (hero == null || !hero.gameObject.activeInHierarchy)
            {
                throw new InvalidOperationException(
                    "An active HeroController is unavailable.");
            }

            var body = hero.GetComponent<Rigidbody2D>();
            if (body == null)
            {
                throw new InvalidOperationException(
                    "The Hero Rigidbody2D is unavailable.");
            }

            var actorState = hero.hero_state;
            if (!Enum.IsDefined(typeof(ActorStates), actorState))
            {
                throw new InvalidOperationException(
                    "HeroController.hero_state is outside the known enum.");
            }

            var position = hero.gameObject.transform.position;
            var velocity = body.velocity;
            builder.AddFloat32("hero.position.x", position.x);
            builder.AddFloat32("hero.position.y", position.y);
            builder.AddFloat32("hero.velocity.x", velocity.x);
            builder.AddFloat32("hero.velocity.y", velocity.y);
            builder.AddString("hero.actorState", actorState.ToString());
            builder.AddBoolean("hero.cState.onGround", hero.cState.onGround);
            builder.AddBoolean("hero.cState.jumping", hero.cState.jumping);
            builder.AddBoolean("hero.cState.falling", hero.cState.falling);
            builder.AddBoolean("hero.cState.dashing", hero.cState.dashing);
            builder.AddBoolean("hero.cState.attacking", hero.cState.attacking);
            builder.AddBoolean("hero.cState.wallSliding", hero.cState.wallSliding);
        }
    }
}
