using System;
using System.Collections.Generic;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.State;

namespace HollowKnightTAS.Core.Movie
{
    public static class MovieProtocolV1
    {
        public const int Version = 1;
        public const string TickUnit = "input";
        public const long DefaultMaxExpandedTicks = 10000000;
        public const int MaximumMarkerUtf8Bytes = 1024;

        private static readonly TasAction[] OrderedActionsValue =
        {
            TasAction.Left,
            TasAction.Right,
            TasAction.Up,
            TasAction.Down,
            TasAction.Jump,
            TasAction.Attack,
            TasAction.Dash,
            TasAction.Cast,
            TasAction.QuickCast,
            TasAction.SuperDash,
            TasAction.DreamNail
        };

        private static readonly string[] OrderedActionNamesValue =
        {
            "left",
            "right",
            "up",
            "down",
            "jump",
            "attack",
            "dash",
            "cast",
            "quickcast",
            "superdash",
            "dreamnail"
        };

        private static readonly HashSet<string> OperatorsValue =
            new HashSet<string>(
                new[] { "==", "!=", "<", "<=", ">", ">=" },
                StringComparer.Ordinal);

        private static readonly HashSet<string> DefaultSemanticPathsValue =
            new HashSet<string>(
                new[]
                {
                    "tick.input",
                    "tick.visual",
                    "tick.fixed",
                    "scene.name",
                    "scene.epoch",
                    "hero.position.x",
                    "hero.position.y",
                    "hero.velocity.x",
                    "hero.velocity.y",
                    "hero.health",
                    "hero.soul",
                    "hero.accepting-input",
                    "hero.dead",
                    "hero.respawning"
                },
                StringComparer.Ordinal);

        static MovieProtocolV1()
        {
            foreach (var key in SemanticSnapshotSchemaV1.Keys)
            {
                DefaultSemanticPathsValue.Add(key);
            }
        }

        public static IReadOnlyList<TasAction> OrderedActions => OrderedActionsValue;
        public static IReadOnlyList<string> OrderedActionNames => OrderedActionNamesValue;
        public static ISet<string> DefaultSemanticPaths =>
            new HashSet<string>(DefaultSemanticPathsValue, StringComparer.Ordinal);

        public static bool TryParseAction(string value, out TasAction action)
        {
            for (var index = 0; index < OrderedActionNamesValue.Length; index++)
            {
                if (string.Equals(
                        value,
                        OrderedActionNamesValue[index],
                        StringComparison.Ordinal))
                {
                    action = OrderedActionsValue[index];
                    return true;
                }
            }

            action = TasAction.None;
            return false;
        }

        public static string GetActionName(TasAction action)
        {
            for (var index = 0; index < OrderedActionsValue.Length; index++)
            {
                if (OrderedActionsValue[index] == action)
                {
                    return OrderedActionNamesValue[index];
                }
            }

            throw new ArgumentOutOfRangeException(
                nameof(action),
                action,
                "Unknown or combined TAS action.");
        }

        public static bool IsOperator(string value)
        {
            return value != null && OperatorsValue.Contains(value);
        }

        public static bool IsIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                var allowed = character >= 'A' && character <= 'Z'
                              || character >= 'a' && character <= 'z'
                              || character >= '0' && character <= '9'
                              || character == '.'
                              || character == '_'
                              || character == '-';
                if (!allowed)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsLowerSha256(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= '0' && character <= '9')
                      || (character >= 'a' && character <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsCanonicalAssertLiteral(string value)
        {
            if (string.Equals(value, "true", StringComparison.Ordinal)
                || string.Equals(value, "false", StringComparison.Ordinal)
                || string.Equals(value, "null", StringComparison.Ordinal))
            {
                return true;
            }

            return IsCanonicalNumber(value);
        }

        public static bool HasValidUtf16(string value)
        {
            if (value == null)
            {
                return false;
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (char.IsHighSurrogate(character))
                {
                    if (index + 1 >= value.Length
                        || !char.IsLowSurrogate(value[index + 1]))
                    {
                        return false;
                    }

                    index++;
                }
                else if (char.IsLowSurrogate(character))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsCanonicalNumber(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var index = 0;
            var negative = false;
            if (value[index] == '-')
            {
                negative = true;
                index++;
                if (index == value.Length)
                {
                    return false;
                }
            }

            if (value[index] == '0')
            {
                index++;
                if (index < value.Length && char.IsDigit(value[index]))
                {
                    return false;
                }
            }
            else
            {
                if (value[index] < '1' || value[index] > '9')
                {
                    return false;
                }

                while (index < value.Length && char.IsDigit(value[index]))
                {
                    index++;
                }
            }

            if (index < value.Length)
            {
                if (value[index] != '.')
                {
                    return false;
                }

                index++;
                var fractionalStart = index;
                while (index < value.Length && char.IsDigit(value[index]))
                {
                    index++;
                }

                if (fractionalStart == index || value[index - 1] == '0')
                {
                    return false;
                }
            }

            if (index != value.Length)
            {
                return false;
            }

            if (negative)
            {
                var zero = true;
                for (var numberIndex = 1; numberIndex < value.Length; numberIndex++)
                {
                    var character = value[numberIndex];
                    if (character >= '1' && character <= '9')
                    {
                        zero = false;
                        break;
                    }
                }

                if (zero)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
