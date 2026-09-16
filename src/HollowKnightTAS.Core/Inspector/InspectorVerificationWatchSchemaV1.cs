using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HollowKnightTAS.Core.State;

namespace HollowKnightTAS.Core.Inspector
{
    public static class InspectorVerificationWatchSchemaV1
    {
        public const int Version = 1;

        private static readonly ReadOnlyDictionary<string, SemanticValueKind>
            StableKindsValue =
                new ReadOnlyDictionary<string, SemanticValueKind>(
                    CreateStableKinds());

        public static IReadOnlyDictionary<string, SemanticValueKind>
            StableKinds => StableKindsValue;

        public static bool IsRegistered(
            WatchDescriptor descriptor)
        {
            if (descriptor == null
                || !descriptor.Key.IsVerificationStable)
            {
                return false;
            }

            return StableKindsValue.TryGetValue(
                       descriptor.Key.Value,
                       out var kind)
                   && kind == descriptor.ValueKind;
        }

        private static Dictionary<string, SemanticValueKind>
            CreateStableKinds()
        {
            var result =
                new Dictionary<string, SemanticValueKind>(
                    StringComparer.Ordinal);
            foreach (var pair in SemanticSnapshotSchemaV1.Kinds)
            {
                result.Add(pair.Key, pair.Value);
            }

            result.Add("tick.input", SemanticValueKind.Int64);
            result.Add("tick.visual", SemanticValueKind.Int64);
            result.Add("tick.fixed", SemanticValueKind.Int64);
            result.Add("tick.movie", SemanticValueKind.Int64);
            result.Add("tick.tft", SemanticValueKind.Float32Bits);
            result.Add("scene.epoch", SemanticValueKind.Int32);
            result.Add(
                "hero.control.acceptingInput",
                SemanticValueKind.Boolean);
            result.Add(
                "hero.control.relinquished",
                SemanticValueKind.Boolean);
            result.Add(
                "hero.animation.controlEnabled",
                SemanticValueKind.Boolean);
            result.Add(
                "hero.animation.clip",
                SemanticValueKind.Utf8String);
            result.Add(
                "hero.animation.frame",
                SemanticValueKind.Int32);
            result.Add(
                "hero.cState.transitioning",
                SemanticValueKind.Boolean);
            result.Add(
                "rng.state.sha256",
                SemanticValueKind.Utf8String);
            result.Add("rng.callCount", SemanticValueKind.Int64);
            AddBossPracticeKinds(result);
            AddCombatKinds(result);
            return result;
        }

        private static void AddCombatKinds(
            IDictionary<string, SemanticValueKind> result)
        {
            foreach (var key in new[]
                     {
                         "combat.primaryBoss.available",
                         "combat.primaryBoss.dead",
                         "combat.contact.overlap"
                     })
            {
                result.Add(key, SemanticValueKind.Boolean);
            }

            result.Add("combat.primaryBoss.hp", SemanticValueKind.Int32);
            result.Add("combat.heroDamage.amount", SemanticValueKind.Int32);
            result.Add("combat.heroDamage.hazardType", SemanticValueKind.Int32);
            result.Add("combat.heroDamage.sequence", SemanticValueKind.Int64);

            foreach (var key in new[]
                     {
                         "combat.primaryBoss.position.x",
                         "combat.primaryBoss.position.y",
                         "combat.primaryBoss.velocity.x",
                         "combat.primaryBoss.velocity.y",
                         "combat.primaryBoss.collider.center.x",
                         "combat.primaryBoss.collider.center.y",
                         "combat.primaryBoss.collider.extents.x",
                         "combat.primaryBoss.collider.extents.y",
                         "combat.relative.delta.x",
                         "combat.relative.delta.y",
                         "combat.relative.distance",
                         "combat.heroDamage.sourcePosition.x",
                         "combat.heroDamage.sourcePosition.y"
                     })
            {
                result.Add(key, SemanticValueKind.Float32Bits);
            }

            foreach (var key in new[]
                     {
                         "combat.primaryBoss.objectPath",
                         "combat.primaryBoss.facing",
                         "combat.primaryBoss.mainFsm",
                         "combat.primaryBoss.mainState",
                         "combat.primaryBoss.recentEvent",
                         "combat.primaryBoss.collidersJson",
                         "combat.hazardsJson",
                         "combat.heroDamage.sourcePath",
                         "combat.heroDamage.sourceName",
                         "combat.heroDamage.side",
                         "combat.relative.horizontalSide"
                     })
            {
                result.Add(key, SemanticValueKind.Utf8String);
            }
        }

        private static void AddBossPracticeKinds(
            IDictionary<string, SemanticValueKind> result)
        {
            foreach (var key in new[]
                     {
                         "bossPractice.bench.atBench",
                         "bossPractice.bench.nearBench",
                         "bossPractice.hero.acceptingInput",
                         "bossPractice.statue.nearest.available",
                         "bossPractice.statue.nearest.unlocked",
                         "bossPractice.statue.nearest.usingDream",
                         "bossPractice.statue.nearest.hasRegular",
                         "bossPractice.statue.nearest.hasDream",
                         "bossPractice.statue.nearest.completedTier1",
                         "bossPractice.statue.nearest.toggle.available",
                         "bossPractice.statue.nearest.toggle.activeSelf",
                         "bossPractice.challengeUi.active",
                         "bossPractice.encounter.active",
                         "bossPractice.encounter.transitionedIn",
                         "bossPractice.boss.available",
                         "bossPractice.boss.dead",
                         "bossPractice.milestone.bossDeathObserved",
                         "bossPractice.milestone.bossesDeadObserved",
                         "bossPractice.milestone.sceneCompleteObserved"
                     })
            {
                result.Add(key, SemanticValueKind.Boolean);
            }

            foreach (var key in new[]
                     {
                         "bossPractice.statues.count",
                         "bossPractice.statue.nearest.toggle.count",
                         "bossPractice.selection.targetLevel",
                         "bossPractice.encounter.level",
                         "bossPractice.encounter.bossCount",
                         "bossPractice.encounter.bossesAlive",
                         "bossPractice.boss.hp",
                         "bossPractice.milestone.terminalLevel"
                     })
            {
                result.Add(key, SemanticValueKind.Int32);
            }

            foreach (var key in new[]
                     {
                         "bossPractice.statue.nearest.position.x",
                         "bossPractice.statue.nearest.position.y",
                         "bossPractice.statue.nearest.distance",
                         "bossPractice.statue.nearest.toggle.position.x",
                         "bossPractice.statue.nearest.toggle.position.y",
                         "bossPractice.statue.nearest.toggle.distance",
                         "bossPractice.boss.position.x",
                         "bossPractice.boss.position.y"
                     })
            {
                result.Add(key, SemanticValueKind.Float32Bits);
            }

            foreach (var key in new[]
                     {
                         "bossPractice.statues.catalogJson",
                         "bossPractice.statue.nearest.scene",
                         "bossPractice.statue.nearest.nameKey",
                         "bossPractice.statue.nearest.completionKey",
                         "bossPractice.statue.nearest.regularScene",
                         "bossPractice.statue.nearest.regularNameKey",
                         "bossPractice.statue.nearest.regularCompletionKey",
                         "bossPractice.statue.nearest.dreamScene",
                         "bossPractice.statue.nearest.dreamNameKey",
                         "bossPractice.statue.nearest.dreamCompletionKey",
                         "bossPractice.statue.nearest.uiFsmState",
                         "bossPractice.statue.nearest.toggle.type",
                         "bossPractice.statue.nearest.toggle.objectPath",
                         "bossPractice.statue.nearest.toggle.catalogJson",
                         "bossPractice.challengeUi.selectedObject",
                         "bossPractice.selection.completionKey",
                         "bossPractice.encounter.scene",
                         "bossPractice.encounter.difficulty",
                         "bossPractice.boss.objectPath",
                         "bossPractice.boss.fsmsJson",
                         "bossPractice.milestone.terminalScene",
                         "bossPractice.milestone.terminalDifficulty"
                     })
            {
                result.Add(key, SemanticValueKind.Utf8String);
            }
        }
    }
}
