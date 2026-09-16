using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GlobalEnums;
using HollowKnightTAS.Core.Inspector;
using HollowKnightTAS.Core.Serialization;
using HollowKnightTAS.Core.State;
using UnityEngine;
using UnityEngine.EventSystems;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HollowKnightTAS.Runtime.Inspector
{
    /// <summary>
    /// Exposes the Hall of Gods statue, challenge-menu, and active boss
    /// encounter state as typed, non-visual Inspector watches. The provider
    /// only reads public game/component state. Death and scene-completion
    /// events are latched so an external client cannot miss a short terminal
    /// transition between samples.
    /// </summary>
    public sealed class BossPracticeWatchProvider :
        IWatchProvider,
        IDisposable
    {
        private const string Prefix = "bossPractice.";
        private const string CombatPrefix = "combat.";
        private readonly WatchDescriptor[] descriptors;
        private readonly Dictionary<int, string> recentFsmEvents =
            new Dictionary<int, string>();
        private BossSceneController? boundController;
        private BossSceneController? lastEncounterController;
        private readonly List<HealthManager> boundBosses =
            new List<HealthManager>();
        private bool bossDeathObserved;
        private bool bossesDeadObserved;
        private bool sceneCompleteObserved;
        private bool leftTerminalScene;
        private string terminalScene = string.Empty;
        private int terminalLevel = -1;
        private long heroDamageSequence;
        private string heroDamageSourcePath = string.Empty;
        private string heroDamageSourceName = string.Empty;
        private string heroDamageSide = string.Empty;
        private int heroDamageAmount;
        private int heroDamageHazardType;
        private Vector2 heroDamageSourcePosition;
        private bool disposed;

        public BossPracticeWatchProvider(int sampleEveryMovieTicks)
        {
            descriptors = new[]
            {
                Descriptor("bench.atBench", SemanticValueKind.Boolean,
                    "PlayerData at bench", sampleEveryMovieTicks),
                Descriptor("bench.nearBench", SemanticValueKind.Boolean,
                    "Hero near bench", sampleEveryMovieTicks),
                Descriptor("hero.acceptingInput", SemanticValueKind.Boolean,
                    "Hero accepting input", sampleEveryMovieTicks),
                Descriptor("statues.count", SemanticValueKind.Int32,
                    "Boss statue count", sampleEveryMovieTicks),
                Descriptor("statues.catalogJson", SemanticValueKind.Utf8String,
                    "Boss statue catalog", sampleEveryMovieTicks),
                Descriptor("statue.nearest.available", SemanticValueKind.Boolean,
                    "Nearest statue available", sampleEveryMovieTicks),
                Descriptor("statue.nearest.scene", SemanticValueKind.Utf8String,
                    "Nearest statue active boss scene", sampleEveryMovieTicks),
                Descriptor("statue.nearest.nameKey", SemanticValueKind.Utf8String,
                    "Nearest statue active name key", sampleEveryMovieTicks),
                Descriptor("statue.nearest.completionKey", SemanticValueKind.Utf8String,
                    "Nearest statue active completion key", sampleEveryMovieTicks),
                Descriptor("statue.nearest.regularScene", SemanticValueKind.Utf8String,
                    "Nearest statue regular boss scene", sampleEveryMovieTicks),
                Descriptor("statue.nearest.regularNameKey", SemanticValueKind.Utf8String,
                    "Nearest statue regular name key", sampleEveryMovieTicks),
                Descriptor("statue.nearest.regularCompletionKey", SemanticValueKind.Utf8String,
                    "Nearest statue regular completion key", sampleEveryMovieTicks),
                Descriptor("statue.nearest.dreamScene", SemanticValueKind.Utf8String,
                    "Nearest statue dream boss scene", sampleEveryMovieTicks),
                Descriptor("statue.nearest.dreamNameKey", SemanticValueKind.Utf8String,
                    "Nearest statue dream name key", sampleEveryMovieTicks),
                Descriptor("statue.nearest.dreamCompletionKey", SemanticValueKind.Utf8String,
                    "Nearest statue dream completion key", sampleEveryMovieTicks),
                Descriptor("statue.nearest.position.x", SemanticValueKind.Float32Bits,
                    "Nearest statue X", sampleEveryMovieTicks),
                Descriptor("statue.nearest.position.y", SemanticValueKind.Float32Bits,
                    "Nearest statue Y", sampleEveryMovieTicks),
                Descriptor("statue.nearest.distance", SemanticValueKind.Float32Bits,
                    "Nearest statue distance", sampleEveryMovieTicks),
                Descriptor("statue.nearest.unlocked", SemanticValueKind.Boolean,
                    "Nearest statue unlocked", sampleEveryMovieTicks),
                Descriptor("statue.nearest.usingDream", SemanticValueKind.Boolean,
                    "Nearest statue dream version", sampleEveryMovieTicks),
                Descriptor("statue.nearest.hasRegular", SemanticValueKind.Boolean,
                    "Nearest statue has regular version", sampleEveryMovieTicks),
                Descriptor("statue.nearest.hasDream", SemanticValueKind.Boolean,
                    "Nearest statue has dream version", sampleEveryMovieTicks),
                Descriptor("statue.nearest.completedTier1", SemanticValueKind.Boolean,
                    "Nearest statue Tier 1 complete", sampleEveryMovieTicks),
                Descriptor("statue.nearest.uiFsmState", SemanticValueKind.Utf8String,
                    "Nearest statue UI FSM state", sampleEveryMovieTicks),
                Descriptor("statue.nearest.toggle.available", SemanticValueKind.Boolean,
                    "Nearest statue version toggle available", sampleEveryMovieTicks),
                Descriptor("statue.nearest.toggle.activeSelf", SemanticValueKind.Boolean,
                    "Nearest statue version toggle active self", sampleEveryMovieTicks),
                Descriptor("statue.nearest.toggle.count", SemanticValueKind.Int32,
                    "Nearest statue version toggle count", sampleEveryMovieTicks),
                Descriptor("statue.nearest.toggle.type", SemanticValueKind.Utf8String,
                    "Nearest statue version toggle type", sampleEveryMovieTicks),
                Descriptor("statue.nearest.toggle.objectPath", SemanticValueKind.Utf8String,
                    "Nearest statue version toggle object path", sampleEveryMovieTicks),
                Descriptor("statue.nearest.toggle.catalogJson", SemanticValueKind.Utf8String,
                    "Nearest statue version toggle catalog", sampleEveryMovieTicks),
                Descriptor("statue.nearest.toggle.position.x", SemanticValueKind.Float32Bits,
                    "Nearest statue version toggle X", sampleEveryMovieTicks),
                Descriptor("statue.nearest.toggle.position.y", SemanticValueKind.Float32Bits,
                    "Nearest statue version toggle Y", sampleEveryMovieTicks),
                Descriptor("statue.nearest.toggle.distance", SemanticValueKind.Float32Bits,
                    "Hero distance to nearest statue version toggle", sampleEveryMovieTicks),
                Descriptor("challengeUi.active", SemanticValueKind.Boolean,
                    "Boss challenge UI active", sampleEveryMovieTicks),
                Descriptor("challengeUi.selectedObject", SemanticValueKind.Utf8String,
                    "Selected challenge UI object", sampleEveryMovieTicks),
                Descriptor("selection.targetLevel", SemanticValueKind.Int32,
                    "Selected boss level", sampleEveryMovieTicks),
                Descriptor("selection.completionKey", SemanticValueKind.Utf8String,
                    "Selected completion key", sampleEveryMovieTicks),
                Descriptor("encounter.active", SemanticValueKind.Boolean,
                    "Boss encounter active", sampleEveryMovieTicks),
                Descriptor("encounter.scene", SemanticValueKind.Utf8String,
                    "Boss encounter scene", sampleEveryMovieTicks),
                Descriptor("encounter.level", SemanticValueKind.Int32,
                    "Boss encounter level", sampleEveryMovieTicks),
                Descriptor("encounter.difficulty", SemanticValueKind.Utf8String,
                    "Boss encounter difficulty", sampleEveryMovieTicks),
                Descriptor("encounter.transitionedIn", SemanticValueKind.Boolean,
                    "Boss encounter transitioned in", sampleEveryMovieTicks),
                Descriptor("encounter.bossCount", SemanticValueKind.Int32,
                    "Boss count", sampleEveryMovieTicks),
                Descriptor("encounter.bossesAlive", SemanticValueKind.Int32,
                    "Bosses alive", sampleEveryMovieTicks),
                Descriptor("boss.available", SemanticValueKind.Boolean,
                    "Primary boss available", sampleEveryMovieTicks),
                Descriptor("boss.objectPath", SemanticValueKind.Utf8String,
                    "Primary boss object path", sampleEveryMovieTicks),
                Descriptor("boss.hp", SemanticValueKind.Int32,
                    "Primary boss HP", sampleEveryMovieTicks),
                Descriptor("boss.dead", SemanticValueKind.Boolean,
                    "Primary boss dead", sampleEveryMovieTicks),
                Descriptor("boss.position.x", SemanticValueKind.Float32Bits,
                    "Primary boss X", sampleEveryMovieTicks),
                Descriptor("boss.position.y", SemanticValueKind.Float32Bits,
                    "Primary boss Y", sampleEveryMovieTicks),
                Descriptor("boss.fsmsJson", SemanticValueKind.Utf8String,
                    "Primary boss FSM states", sampleEveryMovieTicks),
                CombatDescriptor("primaryBoss.available", SemanticValueKind.Boolean,
                    "Primary combat boss available", sampleEveryMovieTicks),
                CombatDescriptor("primaryBoss.objectPath", SemanticValueKind.Utf8String,
                    "Primary combat boss object path", sampleEveryMovieTicks),
                CombatDescriptor("primaryBoss.hp", SemanticValueKind.Int32,
                    "Primary combat boss HP", sampleEveryMovieTicks),
                CombatDescriptor("primaryBoss.dead", SemanticValueKind.Boolean,
                    "Primary combat boss dead", sampleEveryMovieTicks),
                CombatDescriptor("primaryBoss.position.x", SemanticValueKind.Float32Bits,
                    "Primary combat boss X", sampleEveryMovieTicks),
                CombatDescriptor("primaryBoss.position.y", SemanticValueKind.Float32Bits,
                    "Primary combat boss Y", sampleEveryMovieTicks),
                CombatDescriptor("primaryBoss.velocity.x", SemanticValueKind.Float32Bits,
                    "Primary combat boss velocity X", sampleEveryMovieTicks),
                CombatDescriptor("primaryBoss.velocity.y", SemanticValueKind.Float32Bits,
                    "Primary combat boss velocity Y", sampleEveryMovieTicks),
                CombatDescriptor("primaryBoss.facing", SemanticValueKind.Utf8String,
                    "Primary combat boss facing", sampleEveryMovieTicks),
                CombatDescriptor("primaryBoss.mainFsm", SemanticValueKind.Utf8String,
                    "Primary combat boss main FSM", sampleEveryMovieTicks),
                CombatDescriptor("primaryBoss.mainState", SemanticValueKind.Utf8String,
                    "Primary combat boss main state", sampleEveryMovieTicks),
                CombatDescriptor("primaryBoss.recentEvent", SemanticValueKind.Utf8String,
                    "Primary combat boss recent FSM event", sampleEveryMovieTicks),
                CombatDescriptor("primaryBoss.collidersJson", SemanticValueKind.Utf8String,
                    "Primary combat boss collider catalog", sampleEveryMovieTicks),
                CombatDescriptor("primaryBoss.healthManagersJson", SemanticValueKind.Utf8String,
                    "Boss and child health components, not inferred hit eligibility", sampleEveryMovieTicks),
                CombatDescriptor("hero.collidersJson", SemanticValueKind.Utf8String,
                    "Hero collider catalog including input-independent native geometry", sampleEveryMovieTicks),
                CombatDescriptor("hazardsJson", SemanticValueKind.Utf8String,
                    "Active scene DamageHero catalog", sampleEveryMovieTicks),
                CombatDescriptor("heroDamage.sequence", SemanticValueKind.Int64,
                    "Observed Hero damage call sequence", sampleEveryMovieTicks),
                CombatDescriptor("heroDamage.sourcePath", SemanticValueKind.Utf8String,
                    "Most recent Hero damage source path", sampleEveryMovieTicks),
                CombatDescriptor("heroDamage.sourceName", SemanticValueKind.Utf8String,
                    "Most recent Hero damage source name", sampleEveryMovieTicks),
                CombatDescriptor("heroDamage.side", SemanticValueKind.Utf8String,
                    "Most recent Hero damage side", sampleEveryMovieTicks),
                CombatDescriptor("heroDamage.amount", SemanticValueKind.Int32,
                    "Most recent Hero damage amount", sampleEveryMovieTicks),
                CombatDescriptor("heroDamage.hazardType", SemanticValueKind.Int32,
                    "Most recent Hero damage hazard type", sampleEveryMovieTicks),
                CombatDescriptor("heroDamage.sourcePosition.x", SemanticValueKind.Float32Bits,
                    "Most recent Hero damage source X", sampleEveryMovieTicks),
                CombatDescriptor("heroDamage.sourcePosition.y", SemanticValueKind.Float32Bits,
                    "Most recent Hero damage source Y", sampleEveryMovieTicks),
                CombatDescriptor("primaryBoss.collider.center.x", SemanticValueKind.Float32Bits,
                    "Primary combat boss collider center X", sampleEveryMovieTicks),
                CombatDescriptor("primaryBoss.collider.center.y", SemanticValueKind.Float32Bits,
                    "Primary combat boss collider center Y", sampleEveryMovieTicks),
                CombatDescriptor("primaryBoss.collider.extents.x", SemanticValueKind.Float32Bits,
                    "Primary combat boss collider extent X", sampleEveryMovieTicks),
                CombatDescriptor("primaryBoss.collider.extents.y", SemanticValueKind.Float32Bits,
                    "Primary combat boss collider extent Y", sampleEveryMovieTicks),
                CombatDescriptor("relative.delta.x", SemanticValueKind.Float32Bits,
                    "Boss minus Hero X", sampleEveryMovieTicks),
                CombatDescriptor("relative.delta.y", SemanticValueKind.Float32Bits,
                    "Boss minus Hero Y", sampleEveryMovieTicks),
                CombatDescriptor("relative.distance", SemanticValueKind.Float32Bits,
                    "Hero to boss distance", sampleEveryMovieTicks),
                CombatDescriptor("relative.horizontalSide", SemanticValueKind.Utf8String,
                    "Boss horizontal side relative to Hero", sampleEveryMovieTicks),
                CombatDescriptor("contact.overlap", SemanticValueKind.Boolean,
                    "Hero and boss primary colliders overlap", sampleEveryMovieTicks),
                Descriptor("milestone.bossDeathObserved", SemanticValueKind.Boolean,
                    "Boss death observed", sampleEveryMovieTicks),
                Descriptor("milestone.bossesDeadObserved", SemanticValueKind.Boolean,
                    "All bosses dead observed", sampleEveryMovieTicks),
                Descriptor("milestone.sceneCompleteObserved", SemanticValueKind.Boolean,
                    "Boss scene complete observed", sampleEveryMovieTicks),
                Descriptor("milestone.terminalScene", SemanticValueKind.Utf8String,
                    "Terminal encounter scene", sampleEveryMovieTicks),
                Descriptor("milestone.terminalLevel", SemanticValueKind.Int32,
                    "Terminal encounter level", sampleEveryMovieTicks),
                Descriptor("milestone.terminalDifficulty", SemanticValueKind.Utf8String,
                    "Terminal encounter difficulty", sampleEveryMovieTicks)
            };
            On.PlayMakerFSM.SendEvent += OnSendEvent;
            On.HeroController.TakeDamage += OnHeroTakeDamage;
            USceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        public string ProviderId => "boss-practice";

        public IEnumerable<WatchDescriptor> Describe()
        {
            return descriptors;
        }

        public void Sample(
            WatchFrameBuilder builder,
            WatchSampleContext context)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(BossPracticeWatchProvider));
            }

            var player = PlayerData.instance;
            var hero = HeroController.SilentInstance;
            builder.AddBoolean(Prefix + "bench.atBench", player?.atBench ?? false);
            builder.AddBoolean(
                Prefix + "bench.nearBench",
                hero != null && hero.cState.nearBench);
            builder.AddBoolean(
                Prefix + "hero.acceptingInput",
                hero != null && hero.acceptingInput);

            var statues = UnityEngine.Object.FindObjectsOfType<BossStatue>()
                .Where(value => value != null && value.gameObject.activeInHierarchy)
                .OrderBy(StatueSortKey, StringComparer.Ordinal)
                .ThenBy(
                    value => RuntimeWatchIdentity.BuildHierarchyPath(value.transform),
                    StringComparer.Ordinal)
                .ToArray();
            builder.AddInt32(Prefix + "statues.count", statues.Length);
            builder.AddString(
                Prefix + "statues.catalogJson",
                BuildStatueCatalog(statues, hero));

            var nearest = FindNearest(statues, hero);
            WriteNearestStatue(builder, nearest, hero);

            var selected = EventSystem.current?.currentSelectedGameObject;
            var challengeUi = UnityEngine.Object.FindObjectOfType<BossChallengeUI>();
            builder.AddBoolean(
                Prefix + "challengeUi.active",
                challengeUi != null && challengeUi.gameObject.activeInHierarchy);
            builder.AddString(
                Prefix + "challengeUi.selectedObject",
                selected != null ? selected.name ?? string.Empty : string.Empty);
            builder.AddInt32(
                Prefix + "selection.targetLevel",
                player?.bossStatueTargetLevel ?? -1);
            builder.AddString(
                Prefix + "selection.completionKey",
                player?.currentBossStatueCompletionKey ?? string.Empty);

            var controller = BossSceneController.Instance;
            if (controller != null && !controller.gameObject.activeInHierarchy)
            {
                controller = null;
            }
            BindController(controller);

            var scene = USceneManager.GetActiveScene().name ?? string.Empty;
            var encounterActive = controller != null;
            var level = encounterActive ? controller!.BossLevel : -1;
            var bosses = encounterActive
                ? controller!.bosses?.Where(value => value != null).ToArray()
                  ?? Array.Empty<HealthManager>()
                : Array.Empty<HealthManager>();
            var primary = bosses
                .OrderBy(
                    value => RuntimeWatchIdentity.BuildHierarchyPath(value.transform),
                    StringComparer.Ordinal)
                .FirstOrDefault();

            builder.AddBoolean(Prefix + "encounter.active", encounterActive);
            builder.AddString(Prefix + "encounter.scene", encounterActive ? scene : string.Empty);
            builder.AddInt32(Prefix + "encounter.level", level);
            builder.AddString(Prefix + "encounter.difficulty", Difficulty(level));
            builder.AddBoolean(
                Prefix + "encounter.transitionedIn",
                encounterActive && controller!.HasTransitionedIn);
            builder.AddInt32(Prefix + "encounter.bossCount", bosses.Length);
            builder.AddInt32(
                Prefix + "encounter.bossesAlive",
                bosses.Count(value => !value.isDead));
            WritePrimaryBoss(builder, primary);
            WriteCombatState(builder, primary, hero);

            builder.AddBoolean(
                Prefix + "milestone.bossDeathObserved",
                bossDeathObserved);
            builder.AddBoolean(
                Prefix + "milestone.bossesDeadObserved",
                bossesDeadObserved);
            builder.AddBoolean(
                Prefix + "milestone.sceneCompleteObserved",
                sceneCompleteObserved);
            builder.AddString(Prefix + "milestone.terminalScene", terminalScene);
            builder.AddInt32(Prefix + "milestone.terminalLevel", terminalLevel);
            builder.AddString(
                Prefix + "milestone.terminalDifficulty",
                Difficulty(terminalLevel));
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            On.PlayMakerFSM.SendEvent -= OnSendEvent;
            On.HeroController.TakeDamage -= OnHeroTakeDamage;
            USceneManager.activeSceneChanged -= OnActiveSceneChanged;
            UnbindController();
        }

        private void BindController(BossSceneController? controller)
        {
            if (ReferenceEquals(boundController, controller))
            {
                return;
            }

            UnbindController();
            if (controller == null)
            {
                return;
            }

            var currentScene = USceneManager.GetActiveScene().name ?? string.Empty;
            var hasBosses = controller.bosses?.Any(value => value != null) ?? false;
            var startsNewEncounter = hasBosses
                                     && !ReferenceEquals(
                                         lastEncounterController,
                                         controller)
                                     && (string.IsNullOrEmpty(terminalScene)
                                         || !string.Equals(
                                             currentScene,
                                             terminalScene,
                                             StringComparison.Ordinal)
                                         || leftTerminalScene);
            if (startsNewEncounter)
            {
                bossDeathObserved = false;
                bossesDeadObserved = false;
                sceneCompleteObserved = false;
                terminalScene = currentScene;
                terminalLevel = controller.BossLevel;
                leftTerminalScene = false;
            }
            if (hasBosses)
            {
                lastEncounterController = controller;
            }

            boundController = controller;
            if (hasBosses && string.IsNullOrEmpty(terminalScene))
            {
                terminalScene = currentScene;
                terminalLevel = controller.BossLevel;
            }
            controller.OnBossesDead += OnBossesDead;
            controller.OnBossSceneComplete += OnBossSceneComplete;
            foreach (var boss in controller.bosses ?? Array.Empty<HealthManager>())
            {
                if (boss == null)
                {
                    continue;
                }

                boss.OnDeath += OnBossDeath;
                boundBosses.Add(boss);
            }
        }

        private void UnbindController()
        {
            foreach (var boss in boundBosses)
            {
                if (boss != null)
                {
                    boss.OnDeath -= OnBossDeath;
                }
            }
            boundBosses.Clear();
            if (boundController != null)
            {
                boundController.OnBossesDead -= OnBossesDead;
                boundController.OnBossSceneComplete -= OnBossSceneComplete;
            }
            boundController = null;
        }

        private void OnBossDeath()
        {
            bossDeathObserved = true;
            CaptureTerminalIdentity();
        }

        private void OnBossesDead()
        {
            bossesDeadObserved = true;
            CaptureTerminalIdentity();
        }

        private void OnBossSceneComplete()
        {
            sceneCompleteObserved = true;
            CaptureTerminalIdentity();
        }

        private void OnActiveSceneChanged(
            UnityEngine.SceneManagement.Scene previous,
            UnityEngine.SceneManagement.Scene current)
        {
            if (string.IsNullOrEmpty(terminalScene)
                || !string.Equals(
                    previous.name,
                    terminalScene,
                    StringComparison.Ordinal)
                || string.Equals(
                    current.name,
                    terminalScene,
                    StringComparison.Ordinal))
            {
                return;
            }

            leftTerminalScene = true;
            if (bossDeathObserved && bossesDeadObserved)
            {
                // BossSceneController may request the return scene before its
                // OnBossSceneComplete callback is sampled.  Leaving the exact
                // latched encounter only after both independent death signals
                // is the equivalent non-visual completion boundary.
                sceneCompleteObserved = true;
            }
        }

        private void CaptureTerminalIdentity()
        {
            terminalScene = USceneManager.GetActiveScene().name ?? terminalScene;
            if (boundController != null)
            {
                terminalLevel = boundController.BossLevel;
            }
        }

        private static BossStatue? FindNearest(
            IReadOnlyList<BossStatue> statues,
            HeroController? hero)
        {
            if (hero == null || statues.Count == 0)
            {
                return null;
            }

            var position = hero.transform.position;
            return statues
                .OrderBy(value => (value.transform.position - position).sqrMagnitude)
                .ThenBy(StatueSortKey, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private static void WriteNearestStatue(
            WatchFrameBuilder builder,
            BossStatue? statue,
            HeroController? hero)
        {
            var available = statue != null;
            var position = available ? statue!.transform.position : Vector3.zero;
            var usingDream = available
                             && statue!.UsingDreamVersion
                             && statue.HasDreamVersion;
            var state = available
                ? usingDream ? statue!.DreamStatueState : statue!.StatueState
                : BossStatue.Completion.None;
            var distance = available && hero != null
                ? Vector2.Distance(position, hero.transform.position)
                : 0f;
            var toggles = available
                ? FindToggles(statue!)
                : Array.Empty<Component>();
            var toggle = toggles.FirstOrDefault();
            var togglePosition = toggle != null
                ? toggle.transform.position
                : Vector3.zero;
            builder.AddBoolean(Prefix + "statue.nearest.available", available);
            builder.AddString(
                Prefix + "statue.nearest.scene",
                available ? ActiveScene(statue!) : string.Empty);
            builder.AddString(
                Prefix + "statue.nearest.nameKey",
                available ? ActiveNameKey(statue!) : string.Empty);
            builder.AddString(
                Prefix + "statue.nearest.completionKey",
                available ? ActiveCompletionKey(statue!) : string.Empty);
            builder.AddString(
                Prefix + "statue.nearest.regularScene",
                available ? statue!.bossScene?.sceneName ?? string.Empty : string.Empty);
            builder.AddString(
                Prefix + "statue.nearest.regularNameKey",
                available ? statue!.bossDetails.nameKey ?? string.Empty : string.Empty);
            builder.AddString(
                Prefix + "statue.nearest.regularCompletionKey",
                available ? statue!.statueStatePD ?? string.Empty : string.Empty);
            builder.AddString(
                Prefix + "statue.nearest.dreamScene",
                available ? statue!.dreamBossScene?.sceneName ?? string.Empty : string.Empty);
            builder.AddString(
                Prefix + "statue.nearest.dreamNameKey",
                available ? statue!.dreamBossDetails.nameKey ?? string.Empty : string.Empty);
            builder.AddString(
                Prefix + "statue.nearest.dreamCompletionKey",
                available ? statue!.dreamStatueStatePD ?? string.Empty : string.Empty);
            builder.AddFloat32(Prefix + "statue.nearest.position.x", position.x);
            builder.AddFloat32(Prefix + "statue.nearest.position.y", position.y);
            builder.AddFloat32(Prefix + "statue.nearest.distance", distance);
            builder.AddBoolean(
                Prefix + "statue.nearest.unlocked",
                available
                && ((usingDream
                        ? statue!.isAlwaysUnlockedDream
                        : statue!.isAlwaysUnlocked)
                    || state.isUnlocked));
            builder.AddBoolean(
                Prefix + "statue.nearest.usingDream",
                usingDream);
            builder.AddBoolean(
                Prefix + "statue.nearest.hasRegular",
                available && statue!.HasRegularVersion);
            builder.AddBoolean(
                Prefix + "statue.nearest.hasDream",
                available && statue!.HasDreamVersion);
            builder.AddBoolean(
                Prefix + "statue.nearest.completedTier1",
                available && state.completedTier1);
            builder.AddString(
                Prefix + "statue.nearest.uiFsmState",
                available
                    ? statue!.bossUIControlFSM?.ActiveStateName ?? string.Empty
                    : string.Empty);
            builder.AddBoolean(
                Prefix + "statue.nearest.toggle.available",
                toggle != null && toggle.gameObject.activeInHierarchy);
            builder.AddBoolean(
                Prefix + "statue.nearest.toggle.activeSelf",
                toggle != null && toggle.gameObject.activeSelf);
            builder.AddInt32(
                Prefix + "statue.nearest.toggle.count",
                toggles.Length);
            builder.AddString(
                Prefix + "statue.nearest.toggle.type",
                toggle?.GetType().Name ?? string.Empty);
            builder.AddString(
                Prefix + "statue.nearest.toggle.objectPath",
                toggle != null
                    ? RuntimeWatchIdentity.BuildHierarchyPath(toggle.transform)
                    : string.Empty);
            builder.AddString(
                Prefix + "statue.nearest.toggle.catalogJson",
                BuildToggleCatalog(toggles));
            builder.AddFloat32(
                Prefix + "statue.nearest.toggle.position.x",
                togglePosition.x);
            builder.AddFloat32(
                Prefix + "statue.nearest.toggle.position.y",
                togglePosition.y);
            builder.AddFloat32(
                Prefix + "statue.nearest.toggle.distance",
                toggle != null && hero != null
                    ? Vector2.Distance(togglePosition, hero.transform.position)
                    : 0f);
        }

        private static void WritePrimaryBoss(
            WatchFrameBuilder builder,
            HealthManager? boss)
        {
            var available = boss != null;
            var position = available ? boss!.transform.position : Vector3.zero;
            builder.AddBoolean(Prefix + "boss.available", available);
            builder.AddString(
                Prefix + "boss.objectPath",
                available
                    ? RuntimeWatchIdentity.BuildHierarchyPath(boss!.transform)
                    : string.Empty);
            builder.AddInt32(Prefix + "boss.hp", available ? boss!.hp : 0);
            builder.AddBoolean(Prefix + "boss.dead", available && boss!.isDead);
            builder.AddFloat32(Prefix + "boss.position.x", position.x);
            builder.AddFloat32(Prefix + "boss.position.y", position.y);
            builder.AddString(
                Prefix + "boss.fsmsJson",
                available ? BuildFsmCatalog(boss!.gameObject) : "[]");
        }

        private void WriteCombatState(
            WatchFrameBuilder builder,
            HealthManager? boss,
            HeroController? hero)
        {
            var available = boss != null;
            var bossPosition = available
                ? (Vector2)boss!.transform.position
                : Vector2.zero;
            var heroPosition = hero != null
                ? (Vector2)hero.transform.position
                : Vector2.zero;
            var body = available ? boss!.GetComponent<Rigidbody2D>() : null;
            var velocity = body != null ? body.velocity : Vector2.zero;
            var colliders = available
                ? boss!.GetComponents<Collider2D>()
                : Array.Empty<Collider2D>();
            var bossCollider = SelectPrimaryCollider(colliders);
            var heroCollider = hero != null
                ? SelectPrimaryCollider(hero.GetComponents<Collider2D>())
                : null;
            var bounds = bossCollider != null
                ? bossCollider.bounds
                : new Bounds(bossPosition, Vector3.zero);
            var delta = available && hero != null
                ? bossPosition - heroPosition
                : Vector2.zero;
            var fsms = available
                ? boss!.GetComponents<PlayMakerFSM>()
                : Array.Empty<PlayMakerFSM>();
            var indexedFsms = fsms
                .Select((value, index) => new
                {
                    Value = value,
                    Index = index,
                    StateCount = value?.FsmStates?.Length ?? 0
                })
                .Where(value => value.Value != null)
                .OrderByDescending(value => value.StateCount)
                .ThenBy(value => value.Index)
                .ToArray();
            var main = indexedFsms.FirstOrDefault();
            var mainFsm = main?.Value;
            var mainFsmName = mainFsm == null
                ? string.Empty
                : !string.IsNullOrEmpty(mainFsm.FsmName)
                    ? mainFsm.FsmName
                    : "component-" + main!.Index.ToString(
                        CultureInfo.InvariantCulture);
            var recentEvent = mainFsm != null
                              && recentFsmEvents.TryGetValue(
                                  mainFsm.GetInstanceID(),
                                  out var capturedEvent)
                ? capturedEvent
                : "none";

            builder.AddBoolean(CombatPrefix + "primaryBoss.available", available);
            builder.AddString(
                CombatPrefix + "primaryBoss.objectPath",
                available
                    ? RuntimeWatchIdentity.BuildHierarchyPath(boss!.transform)
                    : string.Empty);
            builder.AddInt32(CombatPrefix + "primaryBoss.hp", available ? boss!.hp : 0);
            builder.AddBoolean(CombatPrefix + "primaryBoss.dead", available && boss!.isDead);
            builder.AddFloat32(CombatPrefix + "primaryBoss.position.x", bossPosition.x);
            builder.AddFloat32(CombatPrefix + "primaryBoss.position.y", bossPosition.y);
            builder.AddFloat32(CombatPrefix + "primaryBoss.velocity.x", velocity.x);
            builder.AddFloat32(CombatPrefix + "primaryBoss.velocity.y", velocity.y);
            builder.AddString(
                CombatPrefix + "primaryBoss.facing",
                Facing(available ? boss!.transform : null, velocity.x));
            builder.AddString(CombatPrefix + "primaryBoss.mainFsm", mainFsmName);
            builder.AddString(
                CombatPrefix + "primaryBoss.mainState",
                mainFsm?.ActiveStateName ?? string.Empty);
            builder.AddString(CombatPrefix + "primaryBoss.recentEvent", recentEvent);
            builder.AddString(
                CombatPrefix + "primaryBoss.collidersJson",
                available
                    ? BuildColliderCatalog(boss!, heroCollider, heroPosition)
                    : "[]");
            builder.AddString(
                CombatPrefix + "primaryBoss.healthManagersJson",
                available ? BuildHealthCatalog(boss!) : "[]");
            builder.AddString(
                CombatPrefix + "hero.collidersJson",
                hero != null ? BuildColliderCatalog(hero, heroCollider, heroPosition) : "[]");
            builder.AddString(
                CombatPrefix + "hazardsJson",
                BuildHazardCatalog(heroCollider, heroPosition));
            builder.AddInt64(
                CombatPrefix + "heroDamage.sequence",
                heroDamageSequence);
            builder.AddString(
                CombatPrefix + "heroDamage.sourcePath",
                heroDamageSourcePath);
            builder.AddString(
                CombatPrefix + "heroDamage.sourceName",
                heroDamageSourceName);
            builder.AddString(
                CombatPrefix + "heroDamage.side",
                heroDamageSide);
            builder.AddInt32(
                CombatPrefix + "heroDamage.amount",
                heroDamageAmount);
            builder.AddInt32(
                CombatPrefix + "heroDamage.hazardType",
                heroDamageHazardType);
            builder.AddFloat32(
                CombatPrefix + "heroDamage.sourcePosition.x",
                heroDamageSourcePosition.x);
            builder.AddFloat32(
                CombatPrefix + "heroDamage.sourcePosition.y",
                heroDamageSourcePosition.y);
            builder.AddFloat32(CombatPrefix + "primaryBoss.collider.center.x", bounds.center.x);
            builder.AddFloat32(CombatPrefix + "primaryBoss.collider.center.y", bounds.center.y);
            builder.AddFloat32(CombatPrefix + "primaryBoss.collider.extents.x", bounds.extents.x);
            builder.AddFloat32(CombatPrefix + "primaryBoss.collider.extents.y", bounds.extents.y);
            builder.AddFloat32(CombatPrefix + "relative.delta.x", delta.x);
            builder.AddFloat32(CombatPrefix + "relative.delta.y", delta.y);
            builder.AddFloat32(
                CombatPrefix + "relative.distance",
                available && hero != null ? delta.magnitude : 0f);
            builder.AddString(
                CombatPrefix + "relative.horizontalSide",
                !available || hero == null
                    ? "unavailable"
                    : delta.x < -0.05f
                        ? "left"
                        : delta.x > 0.05f
                            ? "right"
                            : "aligned");
            builder.AddBoolean(
                CombatPrefix + "contact.overlap",
                bossCollider != null
                && heroCollider != null
                && bossCollider.bounds.Intersects(heroCollider.bounds));
        }

        private void OnSendEvent(
            On.PlayMakerFSM.orig_SendEvent original,
            PlayMakerFSM self,
            string eventName)
        {
            if (!disposed && self != null)
            {
                recentFsmEvents[self.GetInstanceID()] = eventName ?? string.Empty;
            }

            original(self, eventName);
        }

        private void OnHeroTakeDamage(
            On.HeroController.orig_TakeDamage original,
            HeroController self,
            GameObject source,
            CollisionSide damageSide,
            int damageAmount,
            int hazardType)
        {
            if (!disposed)
            {
                heroDamageSequence++;
                heroDamageSourcePath = source != null
                    ? RuntimeWatchIdentity.BuildHierarchyPath(source.transform)
                    : string.Empty;
                heroDamageSourceName = source?.name ?? string.Empty;
                heroDamageSide = damageSide.ToString();
                heroDamageAmount = damageAmount;
                heroDamageHazardType = hazardType;
                heroDamageSourcePosition = source != null
                    ? (Vector2)source.transform.position
                    : Vector2.zero;
            }

            original(
                self,
                source,
                damageSide,
                damageAmount,
                hazardType);
        }

        private static Collider2D? SelectPrimaryCollider(
            IEnumerable<Collider2D> colliders, bool includeTriggers = false)
        {
            return colliders
                .Where(value => value != null
                                && value.enabled
                                && value.gameObject.activeInHierarchy
                                && (includeTriggers || !value.isTrigger))
                .OrderByDescending(
                    value => value.bounds.extents.x * value.bounds.extents.y)
                .ThenBy(value => value.GetType().FullName, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private static string BuildColliderCatalog(
            Component root,
            Collider2D? heroCollider,
            Vector2 heroPosition)
        {
            var colliders = root
                .GetComponentsInChildren<Collider2D>(true)
                .Where(value => value != null)
                .Select((value, discoveryIndex) => new
                {
                    Value = value,
                    DiscoveryIndex = discoveryIndex,
                    Path = RuntimeWatchIdentity.BuildHierarchyPath(
                        value.transform)
                })
                .OrderBy(value => value.Path, StringComparer.Ordinal)
                .ThenBy(
                    value => value.Value.GetType().FullName,
                    StringComparer.Ordinal)
                .ThenBy(value => value.DiscoveryIndex)
                .ToArray();
            var builder = new StringBuilder(2048);
            builder.Append('[');
            for (var index = 0; index < colliders.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var item = colliders[index];
                var collider = item.Value;
                var gameObject = collider.gameObject;
                var bounds = collider.bounds;
                var delta = (Vector2)bounds.center - heroPosition;
                builder.Append('{');
                AppendString(builder, "objectPath", item.Path);
                AppendString(builder, "objectName", gameObject.name ?? string.Empty);
                AppendString(builder, "componentType", collider.GetType().FullName ?? string.Empty);
                AppendBoolean(builder, "activeSelf", gameObject.activeSelf);
                AppendBoolean(
                    builder,
                    "activeInHierarchy",
                    gameObject.activeInHierarchy);
                AppendBoolean(builder, "enabled", collider.enabled);
                AppendBoolean(builder, "isTrigger", collider.isTrigger);
                AppendBoolean(builder, "boundsAvailable",
                    collider.enabled && gameObject.activeInHierarchy);
                AppendBoolean(builder, "primaryHeroCollider", collider == heroCollider);
                AppendInt32(builder, "layer", gameObject.layer);
                AppendString(
                    builder,
                    "layerName",
                    LayerMask.LayerToName(gameObject.layer) ?? string.Empty);
                AppendFloat(builder, "centerX", bounds.center.x);
                AppendFloat(builder, "centerY", bounds.center.y);
                AppendFloat(builder, "extentX", bounds.extents.x);
                AppendFloat(builder, "extentY", bounds.extents.y);
                AppendFloat(builder, "deltaX", delta.x);
                AppendFloat(builder, "deltaY", delta.y);
                AppendFloat(builder, "distance", delta.magnitude);
                AppendBoolean(
                    builder,
                    "overlapHero",
                    heroCollider != null
                    && collider.enabled
                    && gameObject.activeInHierarchy
                    && bounds.Intersects(heroCollider.bounds));
                builder.Append('}');
            }
            builder.Append(']');
            return builder.ToString();
        }

        private static string BuildHealthCatalog(HealthManager boss)
        {
            var items = boss.GetComponentsInChildren<HealthManager>(true)
                .Where(value => value != null)
                .OrderBy(value => RuntimeWatchIdentity.BuildHierarchyPath(value.transform),
                    StringComparer.Ordinal).ToArray();
            var builder = new StringBuilder(512);
            builder.Append('[');
            for (var index = 0; index < items.Length; index++)
            {
                if (index > 0) builder.Append(',');
                var health = items[index];
                builder.Append('{');
                AppendString(builder, "objectPath", RuntimeWatchIdentity.BuildHierarchyPath(health.transform));
                AppendString(builder, "objectName", health.gameObject.name ?? string.Empty);
                AppendBoolean(builder, "activeInHierarchy", health.gameObject.activeInHierarchy);
                AppendBoolean(builder, "enabled", health.enabled);
                AppendInt32(builder, "hp", health.hp);
                AppendBoolean(builder, "dead", health.isDead);
                AppendBoolean(builder, "invincible", health.IsInvincible);
                AppendInt32(builder, "invincibleFromDirection", health.InvincibleFromDirection);
                builder.Append('}');
            }
            builder.Append(']');
            return builder.ToString();
        }

        private string BuildHazardCatalog(
            Collider2D? heroCollider,
            Vector2 heroPosition)
        {
            var sceneHandle = USceneManager.GetActiveScene().handle;
            var hazards = UnityEngine.Object.FindObjectsOfType<DamageHero>()
                .Where(value => value != null
                                && value.enabled
                                && value.gameObject.activeInHierarchy
                                && value.gameObject.scene.handle == sceneHandle)
                .Select(value => new
                {
                    Value = value,
                    Path = RuntimeWatchIdentity.BuildHierarchyPath(
                        value.transform)
                })
                .OrderBy(value => value.Path, StringComparer.Ordinal)
                .ToArray();
            var builder = new StringBuilder(4096);
            builder.Append('[');
            for (var index = 0; index < hazards.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var item = hazards[index];
                var hazard = item.Value;
                var gameObject = hazard.gameObject;
                var position = (Vector2)hazard.transform.position;
                var collider = SelectPrimaryCollider(
                    gameObject.GetComponents<Collider2D>(), includeTriggers: true);
                var bounds = collider != null
                    ? collider.bounds
                    : new Bounds(position, Vector3.zero);
                var delta = (Vector2)bounds.center - heroPosition;
                var body = gameObject.GetComponent<Rigidbody2D>();
                var velocity = body != null ? body.velocity : Vector2.zero;
                var fsm = gameObject.GetComponents<PlayMakerFSM>()
                    .Where(value => value != null)
                    .OrderByDescending(
                        value => value.FsmStates?.Length ?? 0)
                    .ThenBy(value => value.FsmName, StringComparer.Ordinal)
                    .FirstOrDefault();
                var recentEvent = fsm != null
                                  && recentFsmEvents.TryGetValue(
                                      fsm.GetInstanceID(),
                                      out var capturedEvent)
                    ? capturedEvent
                    : "none";
                builder.Append('{');
                AppendString(builder, "objectPath", item.Path);
                AppendString(builder, "objectName", gameObject.name ?? string.Empty);
                AppendBoolean(builder, "colliderAvailable", collider != null);
                AppendBoolean(builder, "colliderIsTrigger", collider != null && collider.isTrigger);
                AppendString(builder, "boundsKind", collider != null ? "WorldAabb" : "Unavailable");
                builder.Append(",\"colliders\":");
                builder.Append(BuildColliderCatalog(hazard, heroCollider, heroPosition));
                AppendInt32(builder, "damageDealt", hazard.damageDealt);
                AppendInt32(builder, "hazardType", hazard.hazardType);
                AppendBoolean(
                    builder,
                    "shadowDashHazard",
                    hazard.shadowDashHazard);
                AppendInt32(builder, "layer", gameObject.layer);
                AppendString(
                    builder,
                    "layerName",
                    LayerMask.LayerToName(gameObject.layer) ?? string.Empty);
                AppendFloat(builder, "positionX", position.x);
                AppendFloat(builder, "positionY", position.y);
                AppendFloat(builder, "centerX", bounds.center.x);
                AppendFloat(builder, "centerY", bounds.center.y);
                AppendFloat(builder, "extentX", bounds.extents.x);
                AppendFloat(builder, "extentY", bounds.extents.y);
                AppendFloat(builder, "deltaX", delta.x);
                AppendFloat(builder, "deltaY", delta.y);
                AppendFloat(builder, "distance", delta.magnitude);
                AppendFloat(builder, "velocityX", velocity.x);
                AppendFloat(builder, "velocityY", velocity.y);
                AppendString(builder, "mainFsm", fsm?.FsmName ?? string.Empty);
                AppendString(builder, "mainState", fsm?.ActiveStateName ?? string.Empty);
                AppendString(builder, "recentEvent", recentEvent);
                AppendBoolean(
                    builder,
                    "overlapHero",
                    collider != null
                    && heroCollider != null
                    && bounds.Intersects(heroCollider.bounds));
                builder.Append('}');
            }
            builder.Append(']');
            return builder.ToString();
        }

        private static string Facing(Transform? transform, float velocityX)
        {
            if (velocityX < -0.01f)
            {
                return "left";
            }

            if (velocityX > 0.01f)
            {
                return "right";
            }

            if (transform == null || Math.Abs(transform.localScale.x) < 0.001f)
            {
                return "unknown";
            }

            return transform.localScale.x < 0f ? "left" : "right";
        }

        private static string BuildStatueCatalog(
            IReadOnlyList<BossStatue> statues,
            HeroController? hero)
        {
            var builder = new StringBuilder(4096);
            builder.Append('[');
            for (var index = 0; index < statues.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var statue = statues[index];
                var position = statue.transform.position;
                var usingDream = statue.UsingDreamVersion
                                 && statue.HasDreamVersion;
                var state = usingDream
                    ? statue.DreamStatueState
                    : statue.StatueState;
                var toggle = FindToggles(statue).FirstOrDefault();
                var togglePosition = toggle != null
                    ? toggle.transform.position
                    : Vector3.zero;
                builder.Append('{');
                AppendString(builder, "scene", ActiveScene(statue));
                AppendString(builder, "nameKey", ActiveNameKey(statue));
                AppendString(builder, "completionKey", ActiveCompletionKey(statue));
                AppendString(builder, "regularScene", statue.bossScene?.sceneName ?? string.Empty);
                AppendString(builder, "dreamScene", statue.dreamBossScene?.sceneName ?? string.Empty);
                AppendFloat(builder, "x", position.x);
                AppendFloat(builder, "y", position.y);
                AppendFloat(
                    builder,
                    "distance",
                    hero != null
                        ? Vector2.Distance(position, hero.transform.position)
                        : 0f);
                AppendBoolean(
                    builder,
                    "unlocked",
                    (usingDream
                        ? statue.isAlwaysUnlockedDream
                        : statue.isAlwaysUnlocked)
                    || state.isUnlocked);
                AppendBoolean(builder, "usingDream", usingDream);
                AppendBoolean(builder, "completedTier1", state.completedTier1);
                AppendString(builder, "toggleType", toggle?.GetType().Name ?? string.Empty);
                AppendFloat(builder, "toggleX", togglePosition.x);
                AppendFloat(builder, "toggleY", togglePosition.y);
                builder.Append('}');
            }
            builder.Append(']');
            return builder.ToString();
        }

        private static string BuildFsmCatalog(GameObject gameObject)
        {
            var fsms = gameObject.GetComponents<PlayMakerFSM>()
                .Where(value => value != null)
                .OrderBy(value => value.FsmName, StringComparer.Ordinal)
                .ToArray();
            var builder = new StringBuilder(512);
            builder.Append('[');
            for (var index = 0; index < fsms.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }
                builder.Append('{');
                AppendString(builder, "name", fsms[index].FsmName ?? string.Empty);
                AppendString(builder, "state", fsms[index].ActiveStateName ?? string.Empty);
                AppendBoolean(builder, "active", fsms[index].Active);
                builder.Append('}');
            }
            builder.Append(']');
            return builder.ToString();
        }

        private static string StatueSortKey(BossStatue statue)
        {
            return statue.bossScene?.sceneName
                   ?? statue.statueStatePD
                   ?? string.Empty;
        }

        private static Component[] FindToggles(BossStatue statue)
        {
            return statue.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(value => value is IBossStatueToggle)
                .Cast<Component>()
                .OrderByDescending(value => value.gameObject.activeInHierarchy)
                .ThenBy(
                    value => (value.transform.position
                              - statue.transform.position).sqrMagnitude)
                .ThenBy(
                    value => RuntimeWatchIdentity.BuildHierarchyPath(value.transform),
                    StringComparer.Ordinal)
                .ToArray();
        }

        private static string BuildToggleCatalog(
            IReadOnlyList<Component> toggles)
        {
            var builder = new StringBuilder(256);
            builder.Append('[');
            for (var index = 0; index < toggles.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var toggle = toggles[index];
                var position = toggle.transform.position;
                builder.Append('{');
                AppendString(builder, "type", toggle.GetType().Name);
                AppendString(
                    builder,
                    "objectPath",
                    RuntimeWatchIdentity.BuildHierarchyPath(toggle.transform));
                AppendFloat(builder, "x", position.x);
                AppendFloat(builder, "y", position.y);
                AppendBoolean(builder, "activeSelf", toggle.gameObject.activeSelf);
                AppendBoolean(
                    builder,
                    "activeInHierarchy",
                    toggle.gameObject.activeInHierarchy);
                AppendBoolean(
                    builder,
                    "enabled",
                    !(toggle is Behaviour behaviour) || behaviour.enabled);
                builder.Append('}');
            }
            builder.Append(']');
            return builder.ToString();
        }

        private static string ActiveScene(BossStatue statue)
        {
            return statue.UsingDreamVersion && statue.HasDreamVersion
                ? statue.dreamBossScene?.sceneName ?? string.Empty
                : statue.bossScene?.sceneName ?? string.Empty;
        }

        private static string ActiveNameKey(BossStatue statue)
        {
            return statue.UsingDreamVersion && statue.HasDreamVersion
                ? statue.dreamBossDetails.nameKey ?? string.Empty
                : statue.bossDetails.nameKey ?? string.Empty;
        }

        private static string ActiveCompletionKey(BossStatue statue)
        {
            return statue.UsingDreamVersion && statue.HasDreamVersion
                ? statue.dreamStatueStatePD ?? string.Empty
                : statue.statueStatePD ?? string.Empty;
        }

        private static string Difficulty(int level)
        {
            switch (level)
            {
                case 0:
                    return "Attuned";
                case 1:
                    return "Ascended";
                case 2:
                    return "Radiant";
                default:
                    return string.Empty;
            }
        }

        private static WatchDescriptor Descriptor(
            string suffix,
            SemanticValueKind kind,
            string label,
            int interval)
        {
            return new WatchDescriptor(
                new WatchKey(Prefix + suffix, true),
                kind,
                "boss-practice",
                interval,
                label);
        }

        private static WatchDescriptor CombatDescriptor(
            string suffix,
            SemanticValueKind kind,
            string label,
            int interval)
        {
            return new WatchDescriptor(
                new WatchKey(CombatPrefix + suffix, true),
                kind,
                "combat",
                interval,
                label);
        }

        private static void AppendString(
            StringBuilder builder,
            string name,
            string value)
        {
            AppendPrefix(builder, name);
            CanonicalJsonWriter.AppendString(builder, value ?? string.Empty);
        }

        private static void AppendFloat(
            StringBuilder builder,
            string name,
            float value)
        {
            AppendPrefix(builder, name);
            builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendBoolean(
            StringBuilder builder,
            string name,
            bool value)
        {
            AppendPrefix(builder, name);
            builder.Append(value ? "true" : "false");
        }

        private static void AppendInt32(
            StringBuilder builder,
            string name,
            int value)
        {
            AppendPrefix(builder, name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendPrefix(StringBuilder builder, string name)
        {
            if (builder[builder.Length - 1] != '{')
            {
                builder.Append(',');
            }
            CanonicalJsonWriter.AppendString(builder, name);
            builder.Append(':');
        }
    }
}
