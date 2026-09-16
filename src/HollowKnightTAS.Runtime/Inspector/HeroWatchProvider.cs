using System;
using System.Collections.Generic;
using System.Reflection;
using GlobalEnums;
using HollowKnightTAS.Core.Inspector;
using HollowKnightTAS.Core.State;
using UnityEngine;

namespace HollowKnightTAS.Runtime.Inspector
{
    public sealed class HeroWatchProvider : IWatchProvider
    {
        private static readonly FieldInfo ShadowDashTimerField =
            RequireHeroField("shadowDashTimer");
        private static readonly FieldInfo AttackTimeField =
            RequireHeroField("attack_time");
        private static readonly FieldInfo AttackCooldownField =
            RequireHeroField("attack_cooldown");
        private static readonly FieldInfo RecoilTimerField =
            RequireHeroField("recoilTimer");
        private static readonly FieldInfo RecoilHorizontalTimerField =
            RequireHeroField("recoilHorizontalTimer");
        private static readonly FieldInfo RecoilVectorField =
            RequireHeroField("recoilVector");
        private static readonly FieldInfo JumpStepsField =
            RequireHeroField("jump_steps");
        private static readonly FieldInfo DoubleJumpStepsField =
            RequireHeroField("doubleJump_steps");
        private static readonly FieldInfo NailSlashSlashingField =
            RequireNailSlashField("slashing");
        private static readonly FieldInfo NailSlashStruckField =
            RequireNailSlashField("struck");
        private static readonly FieldInfo NailSlashStepCounterField =
            RequireNailSlashField("stepCounter");

        private readonly WatchDescriptor[] descriptors;

        public HeroWatchProvider(int sampleEveryMovieTicks)
        {
            descriptors = new[]
            {
                Descriptor(
                    "hero.position.x",
                    SemanticValueKind.Float32Bits,
                    "Position X",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.position.y",
                    SemanticValueKind.Float32Bits,
                    "Position Y",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.rigidbody.position.x",
                    SemanticValueKind.Float32Bits,
                    "Rigidbody position X",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.rigidbody.position.y",
                    SemanticValueKind.Float32Bits,
                    "Rigidbody position Y",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.velocity.x",
                    SemanticValueKind.Float32Bits,
                    "Velocity X",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.velocity.y",
                    SemanticValueKind.Float32Bits,
                    "Velocity Y",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.currentVelocity.x",
                    SemanticValueKind.Float32Bits,
                    "Hero current velocity X",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.currentVelocity.y",
                    SemanticValueKind.Float32Bits,
                    "Hero current velocity Y",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.rigidbody.simulated",
                    SemanticValueKind.Boolean,
                    "Rigidbody simulated",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.rigidbody.bodyType",
                    SemanticValueKind.Utf8String,
                    "Rigidbody body type",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.rigidbody.interpolation",
                    SemanticValueKind.Utf8String,
                    "Rigidbody interpolation",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.actorState",
                    SemanticValueKind.Utf8String,
                    "Actor state",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.control.acceptingInput",
                    SemanticValueKind.Boolean,
                    "Accepting input",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.control.relinquished",
                    SemanticValueKind.Boolean,
                    "Control relinquished",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.animation.controlEnabled",
                    SemanticValueKind.Boolean,
                    "Animation control enabled",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.animation.clip",
                    SemanticValueKind.Utf8String,
                    "Animation clip",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.animation.frame",
                    SemanticValueKind.Int32,
                    "Animation frame",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.animation.playing",
                    SemanticValueKind.Boolean,
                    "Animation playing",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.animation.clipTimeSeconds",
                    SemanticValueKind.Float32Bits,
                    "Animation clip time",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.cState.onGround",
                    SemanticValueKind.Boolean,
                    "On ground",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.cState.jumping",
                    SemanticValueKind.Boolean,
                    "Jumping",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.cState.doubleJumping",
                    SemanticValueKind.Boolean,
                    "Double jumping",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.cState.wallJumping",
                    SemanticValueKind.Boolean,
                    "Wall jumping",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.cState.falling",
                    SemanticValueKind.Boolean,
                    "Falling",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.cState.dashing",
                    SemanticValueKind.Boolean,
                    "Dashing",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.cState.shadowDashing",
                    SemanticValueKind.Boolean,
                    "Shade dashing",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.cState.superDashing",
                    SemanticValueKind.Boolean,
                    "Super dashing",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.ability.shadowDashReady",
                    SemanticValueKind.Boolean,
                    "Shade dash ready",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.cooldown.shadowDashSeconds",
                    SemanticValueKind.Float32Bits,
                    "Shade dash cooldown seconds",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.cState.attacking",
                    SemanticValueKind.Boolean,
                    "Attacking",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.cState.upAttacking",
                    SemanticValueKind.Boolean,
                    "Up attacking",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.cState.downAttacking",
                    SemanticValueKind.Boolean,
                    "Down attacking",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.attack.timeSeconds",
                    SemanticValueKind.Float32Bits,
                    "Attack elapsed seconds",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.attack.cooldownSeconds",
                    SemanticValueKind.Float32Bits,
                    "Attack cooldown seconds",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.nailSlash.active",
                    SemanticValueKind.Boolean,
                    "Any nail slash executing",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.nailSlash.direction",
                    SemanticValueKind.Utf8String,
                    "Executing nail slash direction",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.nailSlash.struck",
                    SemanticValueKind.Boolean,
                    "Executing nail slash struck target",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.nailSlash.fixedStep",
                    SemanticValueKind.Int32,
                    "Executing nail slash fixed-step counter",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.cState.recoiling",
                    SemanticValueKind.Boolean,
                    "Damage recoiling",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.cState.recoilingLeft",
                    SemanticValueKind.Boolean,
                    "Recoiling left",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.cState.recoilingRight",
                    SemanticValueKind.Boolean,
                    "Recoiling right",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.cState.recoilFrozen",
                    SemanticValueKind.Boolean,
                    "Recoil frozen",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.recoil.timerSeconds",
                    SemanticValueKind.Float32Bits,
                    "Damage recoil timer",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.recoil.horizontalTimerSeconds",
                    SemanticValueKind.Float32Bits,
                    "Attack recoil timer",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.recoil.vector.x",
                    SemanticValueKind.Float32Bits,
                    "Recoil vector X",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.recoil.vector.y",
                    SemanticValueKind.Float32Bits,
                    "Recoil vector Y",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.jump.steps",
                    SemanticValueKind.Int32,
                    "Jump steps",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.doubleJump.steps",
                    SemanticValueKind.Int32,
                    "Double jump steps",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.cState.wallSliding",
                    SemanticValueKind.Boolean,
                    "Wall sliding",
                    sampleEveryMovieTicks),
                Descriptor(
                    "hero.cState.transitioning",
                    SemanticValueKind.Boolean,
                    "Scene transitioning",
                    sampleEveryMovieTicks),
                Descriptor(
                    "player.health",
                    SemanticValueKind.Int32,
                    "Health",
                    sampleEveryMovieTicks),
                Descriptor(
                    "player.maxHealth",
                    SemanticValueKind.Int32,
                    "Max health",
                    sampleEveryMovieTicks),
                Descriptor(
                    "player.mp",
                    SemanticValueKind.Int32,
                    "Soul",
                    sampleEveryMovieTicks)
            };
        }

        public string ProviderId => "default.hero";

        public IEnumerable<WatchDescriptor> Describe()
        {
            return descriptors;
        }

        public void Sample(
            WatchFrameBuilder builder,
            WatchSampleContext context)
        {
            var hero = HeroController.SilentInstance;
            if (hero == null || !hero.gameObject.activeInHierarchy)
            {
                throw new InvalidOperationException(
                    "An active HeroController is unavailable.");
            }

            var body = hero.GetComponent<Rigidbody2D>()
                       ?? throw new InvalidOperationException(
                           "The Hero Rigidbody2D is unavailable.");
            var player = PlayerData.instance
                         ?? throw new InvalidOperationException(
                             "PlayerData.instance is unavailable.");
            var animation = hero.GetComponent<HeroAnimationController>()
                            ?? throw new InvalidOperationException(
                                "HeroAnimationController is unavailable.");
            var animator = animation.animator
                           ?? throw new InvalidOperationException(
                               "Hero tk2dSpriteAnimator is unavailable.");
            var actorState = hero.hero_state;
            if (!Enum.IsDefined(typeof(ActorStates), actorState))
            {
                throw new InvalidOperationException(
                    "Hero actor state is outside the known enum.");
            }

            var position = hero.transform.position;
            var bodyPosition = body.position;
            var velocity = body.velocity;
            var currentVelocity = hero.current_velocity;
            var shadowDashTimer = ReadSingle(
                ShadowDashTimerField,
                hero);
            var attackTime = ReadSingle(AttackTimeField, hero);
            var attackCooldown = ReadSingle(AttackCooldownField, hero);
            var recoilTimer = ReadSingle(RecoilTimerField, hero);
            var recoilHorizontalTimer = ReadSingle(
                RecoilHorizontalTimerField,
                hero);
            var recoilVector = ReadVector2(RecoilVectorField, hero);
            var jumpSteps = ReadInt32(JumpStepsField, hero);
            var doubleJumpSteps = ReadInt32(DoubleJumpStepsField, hero);
            var slash = CaptureSlashState(hero);
            var hasShadowDash = player.GetBool("hasShadowDash");
            builder.AddFloat32("hero.position.x", position.x);
            builder.AddFloat32("hero.position.y", position.y);
            builder.AddFloat32(
                "hero.rigidbody.position.x",
                bodyPosition.x);
            builder.AddFloat32(
                "hero.rigidbody.position.y",
                bodyPosition.y);
            builder.AddFloat32("hero.velocity.x", velocity.x);
            builder.AddFloat32("hero.velocity.y", velocity.y);
            builder.AddFloat32(
                "hero.currentVelocity.x",
                currentVelocity.x);
            builder.AddFloat32(
                "hero.currentVelocity.y",
                currentVelocity.y);
            builder.AddBoolean(
                "hero.rigidbody.simulated",
                body.simulated);
            builder.AddString(
                "hero.rigidbody.bodyType",
                body.bodyType.ToString());
            builder.AddString(
                "hero.rigidbody.interpolation",
                body.interpolation.ToString());
            builder.AddString(
                "hero.actorState",
                actorState.ToString());
            builder.AddBoolean(
                "hero.control.acceptingInput",
                hero.acceptingInput);
            builder.AddBoolean(
                "hero.control.relinquished",
                hero.controlReqlinquished);
            builder.AddBoolean(
                "hero.animation.controlEnabled",
                animation.controlEnabled);
            builder.AddString(
                "hero.animation.clip",
                animator.CurrentClip?.name ?? string.Empty);
            builder.AddInt32(
                "hero.animation.frame",
                animator.CurrentFrame);
            builder.AddBoolean(
                "hero.animation.playing",
                animator.Playing);
            builder.AddFloat32(
                "hero.animation.clipTimeSeconds",
                animator.ClipTimeSeconds);
            builder.AddBoolean(
                "hero.cState.onGround",
                hero.cState.onGround);
            builder.AddBoolean(
                "hero.cState.jumping",
                hero.cState.jumping);
            builder.AddBoolean(
                "hero.cState.doubleJumping",
                hero.cState.doubleJumping);
            builder.AddBoolean(
                "hero.cState.wallJumping",
                hero.cState.wallJumping);
            builder.AddBoolean(
                "hero.cState.falling",
                hero.cState.falling);
            builder.AddBoolean(
                "hero.cState.dashing",
                hero.cState.dashing);
            builder.AddBoolean(
                "hero.cState.shadowDashing",
                hero.cState.shadowDashing);
            builder.AddBoolean(
                "hero.cState.superDashing",
                hero.cState.superDashing);
            builder.AddBoolean(
                "hero.ability.shadowDashReady",
                hasShadowDash && shadowDashTimer <= 0f);
            builder.AddFloat32(
                "hero.cooldown.shadowDashSeconds",
                Math.Max(0f, shadowDashTimer));
            builder.AddBoolean(
                "hero.cState.attacking",
                hero.cState.attacking);
            builder.AddBoolean(
                "hero.cState.upAttacking",
                hero.cState.upAttacking);
            builder.AddBoolean(
                "hero.cState.downAttacking",
                hero.cState.downAttacking);
            builder.AddFloat32("hero.attack.timeSeconds", attackTime);
            builder.AddFloat32(
                "hero.attack.cooldownSeconds",
                attackCooldown);
            builder.AddBoolean(
                "hero.nailSlash.active",
                slash.Active);
            builder.AddString(
                "hero.nailSlash.direction",
                slash.Direction);
            builder.AddBoolean(
                "hero.nailSlash.struck",
                slash.Struck);
            builder.AddInt32(
                "hero.nailSlash.fixedStep",
                slash.FixedStep);
            builder.AddBoolean(
                "hero.cState.recoiling",
                hero.cState.recoiling);
            builder.AddBoolean(
                "hero.cState.recoilingLeft",
                hero.cState.recoilingLeft);
            builder.AddBoolean(
                "hero.cState.recoilingRight",
                hero.cState.recoilingRight);
            builder.AddBoolean(
                "hero.cState.recoilFrozen",
                hero.cState.recoilFrozen);
            builder.AddFloat32(
                "hero.recoil.timerSeconds",
                recoilTimer);
            builder.AddFloat32(
                "hero.recoil.horizontalTimerSeconds",
                recoilHorizontalTimer);
            builder.AddFloat32(
                "hero.recoil.vector.x",
                recoilVector.x);
            builder.AddFloat32(
                "hero.recoil.vector.y",
                recoilVector.y);
            builder.AddInt32("hero.jump.steps", jumpSteps);
            builder.AddInt32(
                "hero.doubleJump.steps",
                doubleJumpSteps);
            builder.AddBoolean(
                "hero.cState.wallSliding",
                hero.cState.wallSliding);
            builder.AddBoolean(
                "hero.cState.transitioning",
                hero.cState.transitioning);
            builder.AddInt32("player.health", player.health);
            builder.AddInt32("player.maxHealth", player.maxHealth);
            builder.AddInt32("player.mp", player.MPCharge);
        }

        private static WatchDescriptor Descriptor(
            string key,
            SemanticValueKind kind,
            string label,
            int interval)
        {
            return new WatchDescriptor(
                new WatchKey(key, true),
                kind,
                "hero",
                interval,
                label);
        }

        private static FieldInfo RequireHeroField(string name)
        {
            return typeof(HeroController).GetField(
                       name,
                       BindingFlags.Instance
                       | BindingFlags.Public
                       | BindingFlags.NonPublic)
                   ?? throw new MissingFieldException(
                       typeof(HeroController).FullName,
                       name);
        }

        private static FieldInfo RequireNailSlashField(string name)
        {
            return typeof(NailSlash).GetField(
                       name,
                       BindingFlags.Instance
                       | BindingFlags.Public
                       | BindingFlags.NonPublic)
                   ?? throw new MissingFieldException(
                       typeof(NailSlash).FullName,
                       name);
        }

        private static float ReadSingle(
            FieldInfo field,
            object owner)
        {
            var value = field.GetValue(owner);
            if (value is float result)
            {
                return result;
            }

            throw new InvalidOperationException(
                field.DeclaringType?.FullName
                + "."
                + field.Name
                + " is not a float.");
        }

        private static int ReadInt32(
            FieldInfo field,
            object owner)
        {
            var value = field.GetValue(owner);
            if (value is int result)
            {
                return result;
            }

            throw new InvalidOperationException(
                field.DeclaringType?.FullName
                + "."
                + field.Name
                + " is not an Int32.");
        }

        private static Vector2 ReadVector2(
            FieldInfo field,
            object owner)
        {
            var value = field.GetValue(owner);
            if (value is Vector2 result)
            {
                return result;
            }

            throw new InvalidOperationException(
                field.DeclaringType?.FullName
                + "."
                + field.Name
                + " is not a Vector2.");
        }

        private static SlashState CaptureSlashState(HeroController hero)
        {
            var candidates = new[]
            {
                new SlashCandidate("normal", hero.normalSlash),
                new SlashCandidate("alternate", hero.alternateSlash),
                new SlashCandidate("up", hero.upSlash),
                new SlashCandidate("down", hero.downSlash),
                new SlashCandidate("wall", hero.wallSlash)
            };
            foreach (var candidate in candidates)
            {
                if (candidate.Slash == null
                    || !ReadBoolean(
                        NailSlashSlashingField,
                        candidate.Slash))
                {
                    continue;
                }

                return new SlashState(
                    true,
                    candidate.Direction,
                    ReadBoolean(
                        NailSlashStruckField,
                        candidate.Slash),
                    ReadInt32(
                        NailSlashStepCounterField,
                        candidate.Slash));
            }

            return new SlashState(false, "none", false, 0);
        }

        private static bool ReadBoolean(FieldInfo field, object owner)
        {
            var value = field.GetValue(owner);
            if (value is bool result)
            {
                return result;
            }

            throw new InvalidOperationException(
                field.DeclaringType?.FullName
                + "."
                + field.Name
                + " is not a Boolean.");
        }

        private sealed class SlashCandidate
        {
            public SlashCandidate(string direction, NailSlash? slash)
            {
                Direction = direction;
                Slash = slash;
            }

            public string Direction { get; }
            public NailSlash? Slash { get; }
        }

        private sealed class SlashState
        {
            public SlashState(
                bool active,
                string direction,
                bool struck,
                int fixedStep)
            {
                Active = active;
                Direction = direction;
                Struck = struck;
                FixedStep = fixedStep;
            }

            public bool Active { get; }
            public string Direction { get; }
            public bool Struck { get; }
            public int FixedStep { get; }
        }
    }
}
