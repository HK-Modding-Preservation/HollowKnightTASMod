using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HollowKnightTAS.Core.State
{
    public static class SemanticSnapshotSchemaV1
    {
        public const int Version = 1;

        private static readonly ReadOnlyDictionary<string, SemanticValueKind> KindsValue =
            new ReadOnlyDictionary<string, SemanticValueKind>(
                new Dictionary<string, SemanticValueKind>(StringComparer.Ordinal)
                {
                    ["scene.name"] = SemanticValueKind.Utf8String,
                    ["game.state"] = SemanticValueKind.Utf8String,
                    ["hero.position.x"] = SemanticValueKind.Float32Bits,
                    ["hero.position.y"] = SemanticValueKind.Float32Bits,
                    ["hero.velocity.x"] = SemanticValueKind.Float32Bits,
                    ["hero.velocity.y"] = SemanticValueKind.Float32Bits,
                    ["hero.actorState"] = SemanticValueKind.Utf8String,
                    ["hero.cState.onGround"] = SemanticValueKind.Boolean,
                    ["hero.cState.jumping"] = SemanticValueKind.Boolean,
                    ["hero.cState.falling"] = SemanticValueKind.Boolean,
                    ["hero.cState.dashing"] = SemanticValueKind.Boolean,
                    ["hero.cState.attacking"] = SemanticValueKind.Boolean,
                    ["hero.cState.wallSliding"] = SemanticValueKind.Boolean,
                    ["player.health"] = SemanticValueKind.Int32,
                    ["player.maxHealth"] = SemanticValueKind.Int32,
                    ["player.mp"] = SemanticValueKind.Int32
                });

        private static readonly ReadOnlyCollection<string> KeysValue =
            Array.AsReadOnly(
                new[]
                {
                    "game.state",
                    "hero.actorState",
                    "hero.cState.attacking",
                    "hero.cState.dashing",
                    "hero.cState.falling",
                    "hero.cState.jumping",
                    "hero.cState.onGround",
                    "hero.cState.wallSliding",
                    "hero.position.x",
                    "hero.position.y",
                    "hero.velocity.x",
                    "hero.velocity.y",
                    "player.health",
                    "player.maxHealth",
                    "player.mp",
                    "scene.name"
                });

        public static IReadOnlyDictionary<string, SemanticValueKind> Kinds => KindsValue;
        public static IReadOnlyList<string> Keys => KeysValue;

        public static bool TryGetKind(string key, out SemanticValueKind kind)
        {
            if (key == null)
            {
                kind = default;
                return false;
            }

            return KindsValue.TryGetValue(key, out kind);
        }
    }
}
