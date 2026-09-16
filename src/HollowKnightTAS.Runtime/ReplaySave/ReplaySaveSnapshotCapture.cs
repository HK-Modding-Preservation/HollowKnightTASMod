using System;
using GlobalEnums;
using HollowKnightTAS.Core.ReplaySave;
using HollowKnightTAS.Core.State;
using HollowKnightTAS.Runtime.State;
using HollowKnightTAS.Runtime.Rng;
using UnityEngine;

namespace HollowKnightTAS.Runtime.ReplaySave
{
    internal static class ReplaySaveSnapshotCapture
    {
        public const string PositionSourceId =
            "hero-transform-position-v1";

        public static RuntimeSnapshotCapture Create()
        {
            return new RuntimeSnapshotCapture(
                new ISemanticProbe[]
                {
                    new SceneProbe(),
                    new ReplaySaveHeroProbe(),
                    new PlayerDataProbe()
                });
        }

        // Baseline alignment precedes the RNG root handshake and remains v1.
        public static RuntimeSnapshotCapture CreateTarget()
        {
            return new RuntimeSnapshotCapture(new ISemanticProbe[]
            {
                new SceneProbe(), new ReplaySaveHeroProbe(), new PlayerDataProbe(),
                new ReplaySaveRandomProbe(), new ReplaySaveActorsProbe()
            }, SemanticSnapshotSchemas.ReplayTargetWithActors);
        }

        private sealed class ReplaySaveRandomProbe : ISemanticProbe
        {
            private UnityRandomStateCodec_1_5_78_11833? codec;
            public string ProbeId => "replay-save-unity-rng";
            public void Capture(SemanticSnapshotBuilder builder)
            {
                if (codec == null)
                {
                    var resolution = UnityRandomStateCodec_1_5_78_11833.Resolve();
                    codec = resolution.Codec ?? throw new InvalidOperationException(
                        "Replay-save RNG verification is unavailable: " + resolution.Detail);
                }
                // Read only: do not conceal a mismatch by replacing the live state.
                builder.AddString("rng.state.sha256", codec.CaptureCurrent().Sha256);
            }
        }

        private sealed class ReplaySaveHeroProbe : ISemanticProbe
        {
            public string ProbeId => "hero-rigidbody2d";

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

                // Rigidbody2D.position is ahead of the rendered Transform
                // when interpolation is enabled and can differ across clean
                // loads despite an identical stable gameplay state. Replay
                // semantics use the stable Transform position while velocity
                // remains sourced from the physics body.
                var position = hero.gameObject.transform.position;
                var velocity = body.velocity;
                builder.AddFloat32("hero.position.x", position.x);
                builder.AddFloat32("hero.position.y", position.y);
                builder.AddFloat32("hero.velocity.x", velocity.x);
                builder.AddFloat32("hero.velocity.y", velocity.y);
                builder.AddString(
                    "hero.actorState",
                    actorState.ToString());
                builder.AddBoolean(
                    "hero.cState.onGround",
                    hero.cState.onGround);
                builder.AddBoolean(
                    "hero.cState.jumping",
                    hero.cState.jumping);
                builder.AddBoolean(
                    "hero.cState.falling",
                    hero.cState.falling);
                builder.AddBoolean(
                    "hero.cState.dashing",
                    hero.cState.dashing);
                builder.AddBoolean(
                    "hero.cState.attacking",
                    hero.cState.attacking);
                builder.AddBoolean(
                    "hero.cState.wallSliding",
                    hero.cState.wallSliding);
            }
        }
    }
}
