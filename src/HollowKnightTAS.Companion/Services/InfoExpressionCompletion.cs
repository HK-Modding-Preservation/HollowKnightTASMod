using System;
using System.Linq;
using HollowKnightTAS.Core.Inspector;

namespace HollowKnightTAS.Companion.Services
{
    public static class InfoExpressionCompletion
    {
        public static string[] Candidates { get; } = InfoWatchExpression.SnapshotFields.Concat(new[]
        {
            "hero.dashCooldownTimer", "hero.shadowDashTimer", "hero.attack_cooldown", "hero.cState.onGround",
            "hero.cState.facingRight", "hero.cState.jumping", "hero.cState.dashing", "hero.cState.wallSliding",
            "player.health", "player.maxHealth", "player.MPCharge", "player.MPReserve", "player.geo",
            "player.equippedCharms[0]", "game.gameState", "position.x", "position.y", "velocity.x", "velocity.y",
            "component(\"/Knight\", \"HeroController\").jump_steps",
            "fsm(\"/Knight\", \"Spell Control\", \"MP Cost\")", "true", "false"
        }).Distinct().ToArray();

        public static (int Start, int Length, string[] Matches) At(string text, int caret)
        {
            caret = Math.Clamp(caret, 0, text.Length);
            bool quoted = false, escaped = false;
            for (int i = 0; i < caret; i++)
            {
                if (escaped) { escaped = false; continue; }
                if (text[i] == '\\' && quoted) { escaped = true; continue; }
                if (text[i] == '"') quoted = !quoted;
            }
            if (quoted) return (caret, 0, Array.Empty<string>());
            bool Part(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '[' || c == ']';
            int start = caret, end = caret;
            while (start > 0 && Part(text[start - 1])) start--;
            while (end < text.Length && Part(text[end])) end++;
            var prefix = text.Substring(start, caret - start);
            var matches = Candidates.Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
            return (start, end - start, matches);
        }
    }
}
