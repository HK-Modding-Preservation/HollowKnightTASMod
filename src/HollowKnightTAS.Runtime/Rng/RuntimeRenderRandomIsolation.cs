using System;
using HollowKnightTAS.Core.Ipc;
using HollowKnightTAS.Runtime.Companion;
using HollowKnightTAS.Runtime.Control;
using UnityEngine;

namespace HollowKnightTAS.Runtime.Rng
{
    // Only FastNoise rendering is isolated. Native gameplay RNG and all native
    // rendering calls remain untouched outside a verified TAS recording root.
    internal sealed class RuntimeRenderRandomIsolation : IDisposable
    {
        private readonly RuntimeStartupProfileAttestor attestor = new RuntimeStartupProfileAttestor();
        private readonly IsolatedRandomStream<UnityEngine.Random.State> stream =
            new IsolatedRandomStream<UnityEngine.Random.State>(
                () => UnityEngine.Random.state, value => UnityEngine.Random.state = value);
        private readonly Action<string> log;
        private bool active;

        public RuntimeRenderRandomIsolation(Action<string> log)
        {
            this.log = log;
            On.UnityStandardAssets.ImageEffects.FastNoise.DrawNoiseQuadGrid += Draw;
        }

        private void Draw(On.UnityStandardAssets.ImageEffects.FastNoise.orig_DrawNoiseQuadGrid original,
            RenderTexture source, RenderTexture destination, Material material, Texture2D noise,
            int pass, int frameMultiple)
        {
            if (!active && RuntimeVirtualClockBoundary.IsAvailable)
            {
                var identity = attestor.Capture();
                active = identity.Status == StartupProfileAttestationStatus.Verified
                    && identity.RootStatus == StartupRecordingRootStatus.Verified;
                if (active) log("Render RNG isolation enabled: FastNoise private stream v1.");
            }
            if (!active || !RuntimeVirtualClockBoundary.IsAvailable)
            {
                original(source, destination, material, noise, pass, frameMultiple);
                return;
            }
            stream.Run(() => original(source, destination, material, noise, pass, frameMultiple));
        }

        public void Dispose() => On.UnityStandardAssets.ImageEffects.FastNoise.DrawNoiseQuadGrid -= Draw;
    }
}
