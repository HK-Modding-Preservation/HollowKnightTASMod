using System;
using System.Collections.Generic;
using UnityEngine;

namespace HollowKnightTAS.Runtime.Inspector
{
    public sealed class ColliderOverlayRenderer : IDisposable
    {
        private readonly Texture2D lineTexture;
        private bool disposed;

        public ColliderOverlayRenderer()
        {
            lineTexture = new Texture2D(1, 1)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            lineTexture.SetPixel(0, 0, new Color(0f, 1f, 1f, 0.9f));
            lineTexture.Apply(false, true);
        }

        public long RenderCount { get; private set; }

        public void Draw(
            IReadOnlyList<ColliderWatchProvider> providers,
            bool visible)
        {
            if (disposed
                || !visible
                || Event.current.type != EventType.Repaint
                || providers == null
                || providers.Count == 0)
            {
                return;
            }

            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            foreach (var provider in providers)
            {
                if (!provider.IsAlive
                    || !provider.Target.enabled
                    || !provider.Target.gameObject.activeInHierarchy)
                {
                    continue;
                }

                DrawBounds(camera, provider.Target.bounds);
                RenderCount++;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            UnityEngine.Object.Destroy(lineTexture);
        }

        private void DrawBounds(Camera camera, Bounds bounds)
        {
            var minimum = camera.WorldToScreenPoint(
                new Vector3(
                    bounds.min.x,
                    bounds.min.y,
                    bounds.center.z));
            var maximum = camera.WorldToScreenPoint(
                new Vector3(
                    bounds.max.x,
                    bounds.max.y,
                    bounds.center.z));
            if (minimum.z < 0f || maximum.z < 0f)
            {
                return;
            }

            var left = Math.Min(minimum.x, maximum.x);
            var right = Math.Max(minimum.x, maximum.x);
            var top = Screen.height - Math.Max(minimum.y, maximum.y);
            var bottom = Screen.height - Math.Min(minimum.y, maximum.y);
            const float thickness = 2f;
            GUI.DrawTexture(
                new Rect(left, top, right - left, thickness),
                lineTexture);
            GUI.DrawTexture(
                new Rect(left, bottom - thickness, right - left, thickness),
                lineTexture);
            GUI.DrawTexture(
                new Rect(left, top, thickness, bottom - top),
                lineTexture);
            GUI.DrawTexture(
                new Rect(right - thickness, top, thickness, bottom - top),
                lineTexture);
        }
    }
}
