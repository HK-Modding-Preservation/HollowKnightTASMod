using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HollowKnightTAS.Core.State;
using HollowKnightTAS.Runtime.State;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HollowKnightTAS.Runtime.ReplaySave
{
    // Only sampled at save/restore boundaries. Enumerate the explicit active
    // scene, not global resources or prefabs; do not mutate any actor or FSM.
    internal sealed class ReplaySaveActorsProbe : ISemanticProbe
    {
        public string ProbeId => "replay-save-active-health-actors-v1";

        public void Capture(SemanticSnapshotBuilder builder)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Actor scene is not loaded.");
            var actors = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<HealthManager>(false))
                .Where(health => health != null && health.gameObject.activeInHierarchy).ToArray();
            if (actors.Length > 2048) throw new InvalidOperationException("Actor snapshot exceeds its bounded actor count.");
            var records = actors.Select(CaptureActor).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(1); // actor payload format, independent of snapshot schema
                writer.Write(records.Length);
                foreach (var record in records)
                {
                    writer.Write(record);
                    if (stream.Length > 2 * 1024 * 1024) throw new InvalidOperationException("Actor snapshot exceeds its size budget.");
                }
            }
            using var sha = SHA256.Create();
            builder.AddString("scene.activeHealthActors.sha256",
                BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant());
        }

        private static string CaptureActor(HealthManager health)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(PathOf(health.transform));
                writer.Write(health.enabled);
                writer.Write(health.hp);
                writer.Write(health.isDead);
                var position = health.transform.position;
                writer.Write(position.x); writer.Write(position.y);
                var body = health.GetComponent<Rigidbody2D>();
                writer.Write(body != null);
                if (body != null)
                {
                    writer.Write(body.position.x); writer.Write(body.position.y);
                    writer.Write(body.velocity.x); writer.Write(body.velocity.y);
                    writer.Write(body.angularVelocity); writer.Write(body.isKinematic);
                }
                var animator = health.GetComponent<tk2dSpriteAnimator>();
                writer.Write(animator != null);
                if (animator != null)
                {
                    writer.Write(animator.enabled);
                    writer.Write(animator.CurrentClip?.name ?? string.Empty);
                    writer.Write(animator.CurrentFrame);
                }
                var fsms = health.GetComponentsInChildren<PlayMakerFSM>(false)
                    .Select(CaptureFsm).OrderBy(value => value, StringComparer.Ordinal).ToArray();
                writer.Write(fsms.Length);
                foreach (var fsm in fsms) writer.Write(fsm);
            }
            return Convert.ToBase64String(stream.ToArray());
        }

        private static string CaptureFsm(PlayMakerFSM fsm)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(PathOf(fsm.transform)); writer.Write(fsm.FsmName ?? string.Empty);
                writer.Write(fsm.enabled); writer.Write(fsm.ActiveStateName ?? string.Empty);
                var variables = fsm.FsmVariables;
                var ints = variables.IntVariables.OrderBy(v => v.Name, StringComparer.Ordinal).ToArray();
                writer.Write(ints.Length);
                foreach (var value in ints) { writer.Write(value.Name); writer.Write(value.Value); }
                var floats = variables.FloatVariables.OrderBy(v => v.Name, StringComparer.Ordinal).ToArray();
                writer.Write(floats.Length);
                foreach (var value in floats) { writer.Write(value.Name); writer.Write(value.Value); }
                var bools = variables.BoolVariables.OrderBy(v => v.Name, StringComparer.Ordinal).ToArray();
                writer.Write(bools.Length);
                foreach (var value in bools) { writer.Write(value.Name); writer.Write(value.Value); }
            }
            return Convert.ToBase64String(stream.ToArray());
        }

        private static string PathOf(Transform transform)
        {
            var names = new Stack<string>();
            for (var current = transform; current != null; current = current.parent) names.Push(current.name);
            // Length-prefix each name so slashes or repeated sibling names do
            // not create ambiguous byte encodings. Equal-name actors form a
            // sorted multiset; process-local Unity instance IDs are never used.
            return string.Concat(names.Select(name => name.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + name));
        }
    }
}
