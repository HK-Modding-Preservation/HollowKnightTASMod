using System;
using System.Collections.Generic;
using System.Reflection;
using GlobalEnums;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HollowKnightTAS.Runtime.Observation
{
    // Called only by FrameObservationQueue at the native frame boundary, including pause.
    // No world traversal, property invocation, input sampling, or gameplay mutation.
    internal static class RuntimeInfoObservation
    {
        private static readonly FieldInfo? Dash = Field("dashCooldownTimer");
        private static readonly FieldInfo? Shade = Field("shadowDashTimer");
        private static readonly FieldInfo? Attack = Field("attack_cooldown");
        private static FieldInfo? Field(string name) => typeof(HeroController).GetField(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public static Dictionary<string, string> Capture(long nativeFrame, long movieFrame)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            var values = new Dictionary<string, object?>
            {
                ["frame"] = movieFrame, ["nativeFrame"] = nativeFrame, ["room"] = scene
            };
            var hero = HeroController.SilentInstance;
            var manager = GameManager.instance;
            var valid = hero != null && hero.gameObject.activeInHierarchy && manager != null
                && (manager.gameState == GameState.PLAYING || manager.gameState == GameState.PAUSED)
                && scene != "Quit_To_Menu" && !scene.StartsWith("Menu_", StringComparison.Ordinal);
            if (valid)
            {
                var position = hero!.transform.position;
                values["x"] = position.x; values["y"] = position.y;
                var body = hero.GetComponent<Rigidbody2D>();
                if (body != null) { values["vx"] = body.velocity.x; values["vy"] = body.velocity.y; }
                values["dash"] = ReadTimer(Dash, hero);
                values["shade"] = ReadTimer(Shade, hero);
                values["attack"] = ReadTimer(Attack, hero);
                values["grounded"] = hero.cState.onGround;
                values["facingRight"] = hero.cState.facingRight;
                values["jumping"] = hero.cState.jumping;
                values["dashing"] = hero.cState.dashing;
                var player = PlayerData.instance;
                if (player != null)
                {
                    values["health"] = player.health; values["maxHealth"] = player.maxHealth;
                    values["soul"] = player.MPCharge; values["reserveSoul"] = player.MPReserve;
                    if (!player.hasDash) values["dash"] = null;
                    if (!player.hasShadowDash) values["shade"] = null;
                }
            }
            return new Dictionary<string, string>
            {
                ["snapshotId"] = "info-" + Guid.NewGuid().ToString("N"),
                ["snapshotJson"] = JsonConvert.SerializeObject(new { schemaVersion = 1, nativeFrame, movieFrame, values })
            };
        }

        private static object? ReadTimer(FieldInfo? field, HeroController hero)
        {
            if (!(field?.GetValue(hero) is float value) || float.IsNaN(value) || float.IsInfinity(value)) return null;
            return Math.Max(0f, value);
        }
    }
}
