using System;
using System.Linq;
using System.Reflection;
using GlobalEnums;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Verification;
using InControl;
using UnityEngine;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HollowKnightTAS.GameObservation
{
    public sealed class VanillaEquivalenceInputSample
    {
        internal VanillaEquivalenceInputSample(
            int held,
            int pressed,
            int released,
            int axisX,
            int axisY)
        {
            Held = held;
            Pressed = pressed;
            Released = released;
            AxisX = axisX;
            AxisY = axisY;
        }

        public int Held { get; }
        public int Pressed { get; }
        public int Released { get; }
        public int AxisX { get; }
        public int AxisY { get; }
    }

    /// <summary>
    /// Read-only target-build sampler shared by the no-TAS reference observer
    /// and TAS candidate runs. This type has no hooks and exposes no writer.
    /// </summary>
    public sealed class VanillaEquivalenceSampler
    {
        private static readonly FieldInfo AttackTimeField =
            HeroField("attack_time");
        private static readonly FieldInfo AttackCooldownField =
            HeroField("attack_cooldown");
        private static readonly FieldInfo RecoilTimerField =
            HeroField("recoilTimer");
        private static readonly FieldInfo RecoilHorizontalTimerField =
            HeroField("recoilHorizontalTimer");
        private static readonly FieldInfo RecoilVectorField =
            HeroField("recoilVector");
        private static readonly FieldInfo JumpStepsField =
            HeroField("jump_steps");
        private static readonly FieldInfo DoubleJumpStepsField =
            HeroField("doubleJump_steps");
        private static readonly FieldInfo DashTimerField =
            HeroField("dash_timer");
        private static readonly FieldInfo DashCooldownTimerField =
            HeroField("dashCooldownTimer");
        private static readonly FieldInfo ShadowDashTimerField =
            HeroField("shadowDashTimer");
        private static readonly FieldInfo ShadowDashActiveTimerField =
            HeroField("shadow_dash_timer");
        private static readonly FieldInfo DashQueueStepsField =
            HeroField("dashQueueSteps");
        private static readonly FieldInfo DashQueuingField =
            HeroField("dashQueuing");
        private static readonly FieldInfo AirDashedField =
            HeroField("airDashed");
        private static readonly FieldInfo ParryInvulnerabilityTimerField =
            HeroField("parryInvulnTimer");
        private static readonly FieldInfo DashEffectField =
            HeroField("dashEffect");
        private static readonly FieldInfo SlashExecutingField =
            SlashField("slashing");
        private static readonly FieldInfo SlashStruckField =
            SlashField("struck");
        private static readonly FieldInfo SlashStepField =
            SlashField("stepCounter");
        private static readonly FieldInfo RandomState0Field =
            RandomStateField("s0");
        private static readonly FieldInfo RandomState1Field =
            RandomStateField("s1");
        private static readonly FieldInfo RandomState2Field =
            RandomStateField("s2");
        private static readonly FieldInfo RandomState3Field =
            RandomStateField("s3");

        private long sequence;
        private readonly VanillaTimelineAccumulator timeline =
            new VanillaTimelineAccumulator();
        private bool positionOriginCaptured;
        private Vector3 positionOrigin;
        private Vector2 rigidbodyPositionOrigin;
        private bool unityRandomOriginCaptured;
        private int unityRandomOriginS0;
        private int unityRandomOriginS1;
        private int unityRandomOriginS2;
        private int unityRandomOriginS3;

        public void SetUnityRandomSynchronizationOrigin(
            int s0,
            int s1,
            int s2,
            int s3)
        {
            if (unityRandomOriginCaptured)
            {
                if (unityRandomOriginS0 != s0
                    || unityRandomOriginS1 != s1
                    || unityRandomOriginS2 != s2
                    || unityRandomOriginS3 != s3)
                {
                    throw new InvalidOperationException(
                        "Unity RNG synchronization origin changed after capture.");
                }
                return;
            }

            unityRandomOriginS0 = s0;
            unityRandomOriginS1 = s1;
            unityRandomOriginS2 = s2;
            unityRandomOriginS3 = s3;
            unityRandomOriginCaptured = true;
        }

        public VanillaEquivalenceFrame Capture(
            long logicalTick,
            long visualTick,
            long fixedTick,
            VanillaEquivalenceInputSample inputSample)
        {
            var hero = HeroController.SilentInstance
                       ?? throw new InvalidOperationException(
                           "HeroController is unavailable.");
            if (!hero.gameObject.activeInHierarchy)
            {
                throw new InvalidOperationException(
                    "HeroController is inactive.");
            }
            var manager = GameManager.instance
                          ?? throw new InvalidOperationException(
                              "GameManager is unavailable.");
            var player = PlayerData.instance
                         ?? throw new InvalidOperationException(
                             "PlayerData is unavailable.");
            var input = InputHandler.Instance?.inputActions
                        ?? throw new InvalidOperationException(
                            "HeroActions are unavailable.");
            var body = hero.GetComponent<Rigidbody2D>()
                       ?? throw new InvalidOperationException(
                           "Hero Rigidbody2D is unavailable.");
            var animation = hero.GetComponent<HeroAnimationController>()
                            ?? throw new InvalidOperationException(
                                "HeroAnimationController is unavailable.");
            var animator = animation.animator
                           ?? throw new InvalidOperationException(
                               "Hero tk2dSpriteAnimator is unavailable.");
            var scene = USceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.name))
            {
                throw new InvalidOperationException(
                    "The active scene is unavailable.");
            }

            var position = hero.transform.position;
            var bodyPosition = body.position;
            if (!positionOriginCaptured)
            {
                positionOrigin = position;
                rigidbodyPositionOrigin = bodyPosition;
                positionOriginCaptured = true;
            }
            var timelineSample = timeline.Advance(
                fixedTick,
                Time.deltaTime,
                Time.unscaledDeltaTime,
                Time.fixedDeltaTime);
            var timeRaw = Time.time;
            var fixedTimeRaw = Time.fixedTime;

            var builder = new VanillaEquivalenceFrameBuilder();
            AddInput(builder, inputSample);
            builder.AddInt64(
                "diagnostic.inputTick",
                checked((long)InputManager.CurrentTick),
                comparable: false);
            builder.AddInt64(
                "diagnostic.visualTick",
                visualTick,
                comparable: false);
            builder.AddInt64(
                "diagnostic.fixedTick",
                fixedTick,
                comparable: false);
            builder.AddInt32(
                "diagnostic.time.frameCount",
                Time.frameCount,
                comparable: false);
            builder.AddInt32(
                "tick.fixedSteps",
                timelineSample.FixedSteps);
            builder.AddFloat32("time.deltaTime", Time.deltaTime);
            builder.AddFloat32(
                "time.unscaledDeltaTime",
                Time.unscaledDeltaTime);
            builder.AddFloat32(
                "time.fixedDeltaTime",
                Time.fixedDeltaTime);
            builder.AddFloat32(
                "time.captureDeltaTime",
                Time.captureDeltaTime);
            builder.AddFloat32(
                "time.maximumDeltaTime",
                Time.maximumDeltaTime);
            builder.AddFloat32("time.timeScale", Time.timeScale);
            builder.AddInt32(
                "time.targetFrameRate",
                Application.targetFrameRate);
            builder.AddInt32(
                "time.vSyncCount",
                QualitySettings.vSyncCount);
            builder.AddFloat32(
                "time.relative",
                timelineSample.RelativeTime);
            builder.AddFloat32(
                "time.unscaledRelative",
                timelineSample.UnscaledRelativeTime);
            builder.AddFloat32(
                "time.fixedRelative",
                timelineSample.FixedRelativeTime);
            builder.AddFloat32("time.raw", timeRaw);
            builder.AddFloat32("time.fixedRaw", fixedTimeRaw);
            builder.AddFloat32(
                "time.timeMinusFixed",
                timeRaw - fixedTimeRaw);
            builder.AddFloat32(
                "diagnostic.time.raw",
                timeRaw,
                comparable: false);
            builder.AddFloat32(
                "diagnostic.time.fixedRaw",
                fixedTimeRaw,
                comparable: false);
            builder.AddInt64(
                "diagnostic.time.rawDoubleBits",
                BitConverter.DoubleToInt64Bits(Time.timeAsDouble),
                comparable: false);
            builder.AddInt64(
                "diagnostic.time.fixedRawDoubleBits",
                BitConverter.DoubleToInt64Bits(Time.fixedTimeAsDouble),
                comparable: false);
            builder.AddInt64(
                "diagnostic.time.realtimeSinceStartupDoubleBits",
                BitConverter.DoubleToInt64Bits(
                    Time.realtimeSinceStartupAsDouble),
                comparable: false);
            builder.AddFloat32(
                "diagnostic.time.realtimeSinceStartup",
                Time.realtimeSinceStartup,
                comparable: false);
            AddUnityRandomState(builder);

            var velocity = body.velocity;
            var currentVelocity = hero.current_velocity;
            builder.AddString("scene.name", scene.name);
            builder.AddString("game.state", manager.gameState.ToString());
            builder.AddBoolean(
                "game.sceneTransition",
                manager.IsInSceneTransition);
            builder.AddFloat32("hero.position.x", position.x);
            builder.AddFloat32("hero.position.y", position.y);
            builder.AddFloat32(
                "hero.positionDelta.x",
                position.x - positionOrigin.x);
            builder.AddFloat32(
                "hero.positionDelta.y",
                position.y - positionOrigin.y);
            builder.AddFloat32(
                "hero.rigidbody.position.x",
                bodyPosition.x);
            builder.AddFloat32(
                "hero.rigidbody.position.y",
                bodyPosition.y);
            builder.AddFloat32(
                "hero.rigidbody.positionDelta.x",
                bodyPosition.x - rigidbodyPositionOrigin.x);
            builder.AddFloat32(
                "hero.rigidbody.positionDelta.y",
                bodyPosition.y - rigidbodyPositionOrigin.y);
            builder.AddFloat32("hero.velocity.x", velocity.x);
            builder.AddFloat32("hero.velocity.y", velocity.y);
            builder.AddFloat32(
                "hero.currentVelocity.x",
                currentVelocity.x);
            builder.AddFloat32(
                "hero.currentVelocity.y",
                currentVelocity.y);
            builder.AddFloat32(
                "hero.facingScaleX",
                hero.transform.localScale.x);
            builder.AddBoolean(
                "hero.rigidbody.simulated",
                body.simulated);
            builder.AddString(
                "hero.rigidbody.bodyType",
                body.bodyType.ToString());
            builder.AddString("hero.actorState", hero.hero_state.ToString());
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
                "hero.animation.clipTime",
                animator.ClipTimeSeconds);

            AddHeroState(builder, hero);
            AddEffect(
                builder,
                "hero.doubleJump.wings",
                hero.dJumpWingsPrefab,
                position);
            AddEffect(
                builder,
                "hero.doubleJump.flash",
                hero.dJumpFlashPrefab,
                position);
            builder.AddBoolean(
                "hero.doubleJump.feathers.playing",
                hero.dJumpFeathers != null
                && hero.dJumpFeathers.isPlaying,
                comparable: false);
            builder.AddFloat32(
                "hero.attack.time",
                ReadFloat(AttackTimeField, hero));
            builder.AddFloat32(
                "hero.attack.cooldown",
                ReadFloat(AttackCooldownField, hero));
            builder.AddFloat32(
                "hero.recoil.timer",
                ReadFloat(RecoilTimerField, hero));
            builder.AddFloat32(
                "hero.recoil.horizontalTimer",
                ReadFloat(RecoilHorizontalTimerField, hero));
            var recoil = ReadVector2(RecoilVectorField, hero);
            builder.AddFloat32("hero.recoil.vector.x", recoil.x);
            builder.AddFloat32("hero.recoil.vector.y", recoil.y);
            builder.AddInt32(
                "hero.jump.steps",
                ReadInt32(JumpStepsField, hero));
            builder.AddInt32(
                "hero.doubleJump.steps",
                ReadInt32(DoubleJumpStepsField, hero));
            builder.AddFloat32(
                "hero.dash.timer",
                ReadFloat(DashTimerField, hero));
            builder.AddFloat32(
                "hero.dash.cooldownTimer",
                ReadFloat(DashCooldownTimerField, hero));
            builder.AddFloat32(
                "hero.shadowDash.cooldownTimer",
                ReadFloat(ShadowDashTimerField, hero));
            builder.AddFloat32(
                "hero.shadowDash.activeTimer",
                ReadFloat(ShadowDashActiveTimerField, hero));
            builder.AddInt32(
                "hero.dash.queueSteps",
                ReadInt32(DashQueueStepsField, hero));
            builder.AddBoolean(
                "hero.dash.queuing",
                ReadBoolean(DashQueuingField, hero));
            builder.AddBoolean(
                "hero.dash.airDashed",
                ReadBoolean(AirDashedField, hero));
            builder.AddFloat32(
                "hero.damage.parryInvulnerabilityTimer",
                ReadFloat(ParryInvulnerabilityTimerField, hero));
            builder.AddString(
                "hero.damage.mode",
                hero.damageMode.ToString());
            AddEffect(
                builder,
                "hero.dash.effect",
                ReadGameObject(DashEffectField, hero),
                position);
            AddHeroFsm(
                builder,
                "hero.fsm.spellControl",
                hero.spellControl);
            AddHeroFsm(
                builder,
                "hero.fsm.superDash",
                hero.superDash);
            AddHeroFsm(
                builder,
                "hero.fsm.dreamNail",
                FindHeroFsm(hero, "Dream Nail"));
            AddSlash(builder, hero);

            builder.AddBoolean("player.atBench", player.atBench);
            builder.AddInt32("player.health", player.health);
            builder.AddInt32("player.maxHealth", player.maxHealth);
            builder.AddInt32("player.soul", player.MPCharge);
            AddFalseKnight(builder, hero, scene.name);

            return builder.Build(++sequence, logicalTick);
        }

        public static bool HasGameplayInput()
        {
            var actions = InputHandler.Instance?.inputActions;
            return actions != null && ReadBits(
                actions,
                action => action.IsPressed
                          || action.WasPressed
                          || action.WasReleased) != TasAction.None;
        }

        public static VanillaEquivalenceInputSample CaptureInput(
            HeroActions actions)
        {
            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions));
            }

            return new VanillaEquivalenceInputSample(
                (int)ReadBits(actions, action => action.IsPressed),
                (int)ReadBits(actions, action => action.WasPressed),
                (int)ReadBits(actions, action => action.WasReleased),
                Quantize(actions.moveVector.X),
                Quantize(actions.moveVector.Y));
        }

        private static void AddInput(
            VanillaEquivalenceFrameBuilder builder,
            VanillaEquivalenceInputSample input)
        {
            builder.AddInt32("input.held", input.Held);
            builder.AddInt32("input.pressed", input.Pressed);
            builder.AddInt32("input.released", input.Released);
            builder.AddInt32("input.axisX", input.AxisX);
            builder.AddInt32("input.axisY", input.AxisY);
        }

        private static void AddHeroState(
            VanillaEquivalenceFrameBuilder builder,
            HeroController hero)
        {
            builder.AddBoolean("hero.state.onGround", hero.cState.onGround);
            builder.AddBoolean(
                "hero.state.facingRight",
                hero.cState.facingRight);
            builder.AddBoolean("hero.state.jumping", hero.cState.jumping);
            builder.AddBoolean(
                "hero.state.doubleJumping",
                hero.cState.doubleJumping);
            builder.AddBoolean(
                "hero.state.wallJumping",
                hero.cState.wallJumping);
            builder.AddBoolean("hero.state.falling", hero.cState.falling);
            builder.AddBoolean("hero.state.dashing", hero.cState.dashing);
            builder.AddBoolean(
                "hero.state.shadowDashing",
                hero.cState.shadowDashing);
            builder.AddBoolean(
                "hero.state.superDashing",
                hero.cState.superDashing);
            builder.AddBoolean(
                "hero.state.superDashOnWall",
                hero.cState.superDashOnWall);
            builder.AddBoolean(
                "hero.state.backDashing",
                hero.cState.backDashing);
            builder.AddBoolean(
                "hero.state.touchingWall",
                hero.cState.touchingWall);
            builder.AddBoolean("hero.state.attacking", hero.cState.attacking);
            builder.AddBoolean(
                "hero.state.nailCharging",
                hero.cState.nailCharging);
            builder.AddBoolean(
                "hero.state.lookingUp",
                hero.cState.lookingUp);
            builder.AddBoolean(
                "hero.state.lookingDown",
                hero.cState.lookingDown);
            builder.AddBoolean(
                "hero.state.altAttack",
                hero.cState.altAttack);
            builder.AddBoolean(
                "hero.state.upAttacking",
                hero.cState.upAttacking);
            builder.AddBoolean(
                "hero.state.downAttacking",
                hero.cState.downAttacking);
            builder.AddBoolean("hero.state.recoiling", hero.cState.recoiling);
            builder.AddBoolean(
                "hero.state.nailRecoiling",
                hero.cState.recoilingLeft || hero.cState.recoilingRight);
            builder.AddBoolean(
                "hero.state.recoilingLeft",
                hero.cState.recoilingLeft);
            builder.AddBoolean(
                "hero.state.recoilingRight",
                hero.cState.recoilingRight);
            builder.AddBoolean(
                "hero.state.recoilFrozen",
                hero.cState.recoilFrozen);
            builder.AddBoolean(
                "hero.state.invulnerable",
                hero.cState.invulnerable);
            builder.AddBoolean(
                "hero.state.casting",
                hero.cState.casting);
            builder.AddBoolean(
                "hero.state.castRecoiling",
                hero.cState.castRecoiling);
            builder.AddBoolean(
                "hero.state.bouncing",
                hero.cState.bouncing);
            builder.AddBoolean("hero.state.dead", hero.cState.dead);
            builder.AddBoolean(
                "hero.state.hazardDeath",
                hero.cState.hazardDeath);
            builder.AddBoolean(
                "hero.state.hazardRespawning",
                hero.cState.hazardRespawning);
            builder.AddBoolean(
                "hero.state.willHardLand",
                hero.cState.willHardLand);
            builder.AddBoolean(
                "hero.state.preventDash",
                hero.cState.preventDash);
            builder.AddBoolean(
                "hero.state.dashCooldown",
                hero.cState.dashCooldown);
            builder.AddBoolean(
                "hero.state.wallSliding",
                hero.cState.wallSliding);
            builder.AddBoolean(
                "hero.state.transitioning",
                hero.cState.transitioning);
            builder.AddBoolean(
                "hero.state.spellQuake",
                hero.cState.spellQuake);
            builder.AddBoolean(
                "hero.state.freezeCharge",
                hero.cState.freezeCharge);
            builder.AddBoolean(
                "hero.state.focusing",
                hero.cState.focusing);
            builder.AddBoolean(
                "hero.state.wasOnGround",
                hero.cState.wasOnGround);
        }

        private static void AddHeroFsm(
            VanillaEquivalenceFrameBuilder builder,
            string prefix,
            PlayMakerFSM? fsm)
        {
            builder.AddBoolean(prefix + ".present", fsm != null);
            builder.AddBoolean(prefix + ".active", fsm != null && fsm.Active);
            builder.AddString(prefix + ".name", fsm?.FsmName ?? string.Empty);
            builder.AddString(
                prefix + ".state",
                fsm?.ActiveStateName ?? string.Empty);
        }

        private static PlayMakerFSM? FindHeroFsm(
            HeroController hero,
            string fsmName)
        {
            return hero.GetComponentsInChildren<PlayMakerFSM>(true)
                .Where(value => value != null)
                .OrderBy(value => HierarchyPath(value.transform),
                    StringComparer.Ordinal)
                .ThenBy(value => value.FsmName, StringComparer.Ordinal)
                .FirstOrDefault(value => string.Equals(
                    value.FsmName,
                    fsmName,
                    StringComparison.Ordinal));
        }

        private static void AddFalseKnight(
            VanillaEquivalenceFrameBuilder builder,
            HeroController hero,
            string sceneName)
        {
            var health = FindFalseKnight(sceneName);
            var present = health != null;
            var transform = health?.transform;
            var position = transform?.position ?? Vector3.zero;
            var body = health?.GetComponent<Rigidbody2D>();
            var velocity = body?.velocity ?? Vector2.zero;
            var fsm = health?.GetComponents<PlayMakerFSM>()
                .Where(value => value != null)
                .OrderBy(value => value.FsmName, StringComparer.Ordinal)
                .FirstOrDefault(value => string.Equals(
                    value.FsmName,
                    "FalseyControl",
                    StringComparison.Ordinal));
            var animator = health?.GetComponent<tk2dSpriteAnimator>();
            var colliders = health == null
                ? Array.Empty<Collider2D>()
                : health.GetComponentsInChildren<Collider2D>(true)
                    .Where(value => value != null)
                    .OrderBy(value => HierarchyPath(value.transform),
                        StringComparer.Ordinal)
                    .ThenBy(
                        value => value.GetType().FullName,
                        StringComparer.Ordinal)
                    .ToArray();
            var hitter = health == null
                ? null
                : health.GetComponentsInChildren<Transform>(true)
                    .Where(value => value != null)
                    .OrderBy(HierarchyPath, StringComparer.Ordinal)
                    .FirstOrDefault(value => string.Equals(
                        value.gameObject.name,
                        "Hitter",
                        StringComparison.Ordinal));
            var hitterCollider = hitter?.GetComponent<Collider2D>();
            var hitterDamage = hitter?.GetComponent<DamageHero>();
            var heroPosition = hero.transform.position;
            var relative = present
                ? position - heroPosition
                : Vector3.zero;

            builder.AddBoolean("world.falseKnight.present", present);
            builder.AddString(
                "world.falseKnight.objectName",
                health?.gameObject.name ?? string.Empty);
            builder.AddString(
                "world.falseKnight.objectPath",
                transform == null ? string.Empty : HierarchyPath(transform));
            builder.AddInt32("world.falseKnight.hp", health?.hp ?? 0);
            builder.AddBoolean(
                "world.falseKnight.dead",
                health?.isDead == true);
            builder.AddBoolean(
                "world.falseKnight.invincible",
                health?.IsInvincible == true);
            builder.AddInt32(
                "world.falseKnight.lastAttackDirection",
                health?.GetAttackDirection() ?? 0);
            builder.AddFloat32("world.falseKnight.position.x", position.x);
            builder.AddFloat32("world.falseKnight.position.y", position.y);
            builder.AddFloat32("world.falseKnight.velocity.x", velocity.x);
            builder.AddFloat32("world.falseKnight.velocity.y", velocity.y);
            builder.AddBoolean(
                "world.falseKnight.rigidbody.present",
                body != null);
            builder.AddBoolean(
                "world.falseKnight.rigidbody.simulated",
                body != null && body.simulated);
            builder.AddString(
                "world.falseKnight.rigidbody.bodyType",
                body?.bodyType.ToString() ?? string.Empty);
            builder.AddBoolean(
                "world.falseKnight.fsm.present",
                fsm != null);
            builder.AddBoolean(
                "world.falseKnight.fsm.active",
                fsm != null && fsm.Active);
            builder.AddString(
                "world.falseKnight.fsm.name",
                fsm?.FsmName ?? string.Empty);
            builder.AddString(
                "world.falseKnight.fsm.state",
                fsm?.ActiveStateName ?? string.Empty);
            builder.AddBoolean(
                "world.falseKnight.animation.present",
                animator != null);
            builder.AddString(
                "world.falseKnight.animation.clip",
                animator?.CurrentClip?.name ?? string.Empty);
            builder.AddInt32(
                "world.falseKnight.animation.frame",
                animator?.CurrentFrame ?? -1);
            builder.AddBoolean(
                "world.falseKnight.animation.playing",
                animator?.Playing == true);
            builder.AddInt32(
                "world.falseKnight.collider.count",
                colliders.Length);
            builder.AddInt32(
                "world.falseKnight.collider.enabledCount",
                colliders.Count(value => value.enabled));
            builder.AddInt32(
                "world.falseKnight.collider.activeCount",
                colliders.Count(value => value.gameObject.activeInHierarchy));
            builder.AddBoolean(
                "world.falseKnight.hitter.present",
                hitter != null);
            builder.AddBoolean(
                "world.falseKnight.hitter.active",
                hitter?.gameObject.activeInHierarchy == true);
            builder.AddBoolean(
                "world.falseKnight.hitter.colliderEnabled",
                hitterCollider?.enabled == true);
            builder.AddInt32(
                "world.falseKnight.hitter.damage",
                hitterDamage?.damageDealt ?? 0);
            builder.AddFloat32(
                "world.falseKnight.relative.x",
                relative.x);
            builder.AddFloat32(
                "world.falseKnight.relative.y",
                relative.y);
        }

        private static HealthManager? FindFalseKnight(string sceneName)
        {
            if (!string.Equals(
                    sceneName,
                    "GG_False_Knight",
                    StringComparison.Ordinal))
            {
                return null;
            }

            return UnityEngine.Object.FindObjectsOfType<HealthManager>()
                .Where(value => value != null
                                && string.Equals(
                                    value.gameObject.name,
                                    "False Knight New",
                                    StringComparison.Ordinal))
                .OrderBy(value => HierarchyPath(value.transform),
                    StringComparer.Ordinal)
                .FirstOrDefault(value => value
                    .GetComponents<PlayMakerFSM>()
                    .Any(fsm => fsm != null && string.Equals(
                        fsm.FsmName,
                        "FalseyControl",
                        StringComparison.Ordinal)));
        }

        private static string HierarchyPath(Transform transform)
        {
            var path = transform.gameObject.name ?? string.Empty;
            var parent = transform.parent;
            while (parent != null)
            {
                path = (parent.gameObject.name ?? string.Empty) + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        private void AddUnityRandomState(
            VanillaEquivalenceFrameBuilder builder)
        {
            object boxed = UnityEngine.Random.state;
            var currentS0 = ReadInt32(RandomState0Field, boxed);
            var currentS1 = ReadInt32(RandomState1Field, boxed);
            var currentS2 = ReadInt32(RandomState2Field, boxed);
            var currentS3 = ReadInt32(RandomState3Field, boxed);
            if (!unityRandomOriginCaptured)
            {
                throw new InvalidOperationException(
                    "Unity RNG synchronization origin was not supplied by "
                    + "the external clock payload.");
            }

            // The state read immediately after the external InitState call is
            // authoritative. The live global Unity RNG state remains useful
            // diagnostics, but vanilla render/particle cadence can advance it
            // before the completed-frame sample without changing gameplay.
            builder.AddInt32(
                "world.rng.unity.synchronizedOrigin.s0",
                unityRandomOriginS0);
            builder.AddInt32(
                "world.rng.unity.synchronizedOrigin.s1",
                unityRandomOriginS1);
            builder.AddInt32(
                "world.rng.unity.synchronizedOrigin.s2",
                unityRandomOriginS2);
            builder.AddInt32(
                "world.rng.unity.synchronizedOrigin.s3",
                unityRandomOriginS3);
            builder.AddInt32(
                "diagnostic.world.rng.unity.current.s0",
                currentS0,
                comparable: false);
            builder.AddInt32(
                "diagnostic.world.rng.unity.current.s1",
                currentS1,
                comparable: false);
            builder.AddInt32(
                "diagnostic.world.rng.unity.current.s2",
                currentS2,
                comparable: false);
            builder.AddInt32(
                "diagnostic.world.rng.unity.current.s3",
                currentS3,
                comparable: false);
        }

        private static void AddSlash(
            VanillaEquivalenceFrameBuilder builder,
            HeroController hero)
        {
            var slashes = new[]
            {
                new Slash("normal", hero.normalSlash),
                new Slash("alternate", hero.alternateSlash),
                new Slash("up", hero.upSlash),
                new Slash("down", hero.downSlash),
                new Slash("wall", hero.wallSlash)
            };
            foreach (var slash in slashes)
            {
                AddSlashState(
                    builder,
                    "hero.slash." + slash.Direction,
                    slash.Value);
                if (slash.Value == null
                    || !ReadBoolean(SlashExecutingField, slash.Value))
                {
                    continue;
                }

                builder.AddBoolean("hero.slash.executing", true);
                builder.AddString("hero.slash.direction", slash.Direction);
                builder.AddBoolean(
                    "hero.slash.struck",
                    ReadBoolean(SlashStruckField, slash.Value));
                builder.AddInt32(
                    "hero.slash.fixedStep",
                    ReadInt32(SlashStepField, slash.Value));
                return;
            }

            builder.AddBoolean("hero.slash.executing", false);
            builder.AddString("hero.slash.direction", "none");
            builder.AddBoolean("hero.slash.struck", false);
            builder.AddInt32("hero.slash.fixedStep", 0);
        }

        private static void AddSlashState(
            VanillaEquivalenceFrameBuilder builder,
            string prefix,
            NailSlash? slash)
        {
            builder.AddBoolean(prefix + ".present", slash != null);
            builder.AddBoolean(
                prefix + ".activeSelf",
                slash != null && slash.gameObject.activeSelf);
            builder.AddBoolean(
                prefix + ".activeInHierarchy",
                slash != null && slash.gameObject.activeInHierarchy);
            builder.AddBoolean(
                prefix + ".executing",
                slash != null
                && ReadBoolean(SlashExecutingField, slash));
            builder.AddBoolean(
                prefix + ".struck",
                slash != null
                && ReadBoolean(SlashStruckField, slash));
            builder.AddInt32(
                prefix + ".fixedStep",
                slash == null ? 0 : ReadInt32(SlashStepField, slash));
        }

        private static void AddEffect(
            VanillaEquivalenceFrameBuilder builder,
            string prefix,
            GameObject? effect,
            Vector3 heroPosition)
        {
            builder.AddBoolean(prefix + ".present", effect != null);
            builder.AddBoolean(
                prefix + ".activeSelf",
                effect != null && effect.activeSelf);
            builder.AddBoolean(
                prefix + ".activeInHierarchy",
                effect != null && effect.activeInHierarchy);
            var position = effect == null
                ? heroPosition
                : effect.transform.position;
            builder.AddFloat32(
                prefix + ".offset.x",
                position.x - heroPosition.x);
            builder.AddFloat32(
                prefix + ".offset.y",
                position.y - heroPosition.y);
        }

        private static TasAction ReadBits(
            HeroActions actions,
            Func<PlayerAction, bool> predicate)
        {
            var value = TasAction.None;
            Add(TasAction.Left, actions.left);
            Add(TasAction.Right, actions.right);
            Add(TasAction.Up, actions.up);
            Add(TasAction.Down, actions.down);
            Add(TasAction.Jump, actions.jump);
            Add(TasAction.Attack, actions.attack);
            Add(TasAction.Dash, actions.dash);
            Add(TasAction.Cast, actions.cast);
            Add(TasAction.QuickCast, actions.quickCast);
            Add(TasAction.SuperDash, actions.superDash);
            Add(TasAction.DreamNail, actions.dreamNail);
            return value;

            void Add(TasAction bit, PlayerAction action)
            {
                if (predicate(action))
                {
                    value |= bit;
                }
            }
        }

        private static int Quantize(float value)
        {
            return (int)Math.Round(
                Math.Max(-1f, Math.Min(1f, value))
                * InputSample.AxisScale,
                MidpointRounding.AwayFromZero);
        }

        private static FieldInfo HeroField(string name)
        {
            return RequireField(typeof(HeroController), name);
        }

        private static FieldInfo SlashField(string name)
        {
            return RequireField(typeof(NailSlash), name);
        }

        private static FieldInfo RandomStateField(string name)
        {
            return RequireField(typeof(UnityEngine.Random.State), name);
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            return type.GetField(
                       name,
                       BindingFlags.Instance
                       | BindingFlags.Public
                       | BindingFlags.NonPublic)
                   ?? throw new MissingFieldException(type.FullName, name);
        }

        private static float ReadFloat(FieldInfo field, object owner)
        {
            return field.GetValue(owner) is float value
                ? value
                : throw WrongFieldType(field, "Single");
        }

        private static int ReadInt32(FieldInfo field, object owner)
        {
            return field.GetValue(owner) is int value
                ? value
                : throw WrongFieldType(field, "Int32");
        }

        private static bool ReadBoolean(FieldInfo field, object owner)
        {
            return field.GetValue(owner) is bool value
                ? value
                : throw WrongFieldType(field, "Boolean");
        }

        private static Vector2 ReadVector2(FieldInfo field, object owner)
        {
            return field.GetValue(owner) is Vector2 value
                ? value
                : throw WrongFieldType(field, "Vector2");
        }

        private static GameObject? ReadGameObject(
            FieldInfo field,
            object owner)
        {
            var value = field.GetValue(owner);
            if (value == null)
            {
                return null;
            }
            if (value is GameObject gameObject)
            {
                return gameObject;
            }
            throw WrongFieldType(field, "GameObject");
        }

        private static InvalidOperationException WrongFieldType(
            FieldInfo field,
            string expected)
        {
            return new InvalidOperationException(
                field.DeclaringType?.FullName
                + "."
                + field.Name
                + " is not "
                + expected
                + ".");
        }

        private sealed class Slash
        {
            public Slash(string direction, NailSlash? value)
            {
                Direction = direction;
                Value = value;
            }

            public string Direction { get; }
            public NailSlash? Value { get; }
        }
    }
}
