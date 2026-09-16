using System;
using System.Collections.Generic;
using System.Linq;

namespace HollowKnightTAS.Core.State
{
    public static class SemanticSnapshotSchemas
    {
        public const int ReplayTargetWithRandomState = 2;
        public const int ReplayTargetWithActors = 3;
        private static readonly IReadOnlyList<string> TargetKeys = Array.AsReadOnly(
            SemanticSnapshotSchemaV1.Keys.Concat(new[] { "rng.state.sha256" })
                .OrderBy(key => key, StringComparer.Ordinal).ToArray());
        private static readonly IReadOnlyList<string> ActorTargetKeys = Array.AsReadOnly(
            TargetKeys.Concat(new[] { "scene.activeHealthActors.sha256" })
                .OrderBy(key => key, StringComparer.Ordinal).ToArray());

        public static bool IsSupported(int version) =>
            version == SemanticSnapshotSchemaV1.Version || version == ReplayTargetWithRandomState || version == ReplayTargetWithActors;

        public static IReadOnlyList<string> Keys(int version)
        {
            if (!IsSupported(version)) throw new ArgumentOutOfRangeException(nameof(version));
            return version == ReplayTargetWithActors ? ActorTargetKeys
                : version == ReplayTargetWithRandomState ? TargetKeys : SemanticSnapshotSchemaV1.Keys;
        }

        public static bool TryGetKind(int version, string key, out SemanticValueKind kind)
        {
            if (!IsSupported(version)) throw new ArgumentOutOfRangeException(nameof(version));
            if ((version >= ReplayTargetWithRandomState && key == "rng.state.sha256")
                || (version == ReplayTargetWithActors && key == "scene.activeHealthActors.sha256"))
            {
                kind = SemanticValueKind.Utf8String;
                return true;
            }
            return SemanticSnapshotSchemaV1.TryGetKind(key, out kind);
        }
    }
}
