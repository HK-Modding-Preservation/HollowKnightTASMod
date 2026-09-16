using System;
using System.Globalization;
using System.Text;
using HollowKnightTAS.Core.Inspector;
using UnityEngine;

namespace HollowKnightTAS.Runtime.Inspector
{
    internal static class RuntimeWatchIdentity
    {
        public static WatchKey StableComponent(
            Component component,
            string suffix)
        {
            if (component == null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            var gameObject = component.gameObject;
            var scene = gameObject.scene;
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.name))
            {
                throw new InvalidOperationException(
                    "Stable component keys require a valid scene.");
            }

            var ordinal = GetComponentOrdinal(component);
            return new WatchKey(
                "component/"
                + Escape(scene.name)
                + "/"
                + BuildHierarchyPath(component.transform)
                + "/"
                + Escape(component.GetType().FullName)
                + "/"
                + ordinal.ToString(CultureInfo.InvariantCulture)
                + "/"
                + Escape(suffix),
                true);
        }

        public static string StableComponentPrefix(Component component)
        {
            var key = StableComponent(component, "value").Value;
            return key.Substring(
                0,
                key.Length - "/value".Length);
        }

        public static string StableHeroPrefix(
            string componentKind,
            int ordinal)
        {
            return "semantic/hero/"
                   + Escape(componentKind)
                   + "/"
                   + ordinal.ToString(CultureInfo.InvariantCulture);
        }

        public static string DisplayPrefix(
            int sceneEpoch,
            string kind,
            long registrationSequence)
        {
            return "display/scene-"
                   + sceneEpoch.ToString(CultureInfo.InvariantCulture)
                   + "/"
                   + Escape(kind)
                   + "-"
                   + registrationSequence.ToString(
                       CultureInfo.InvariantCulture);
        }

        public static string BuildHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                throw new ArgumentNullException(nameof(transform));
            }

            var segments = new System.Collections.Generic.List<string>();
            var current = transform;
            while (current != null)
            {
                segments.Add(
                    Escape(current.gameObject.name)
                    + "-"
                    + SameNameOrdinal(current).ToString(
                        CultureInfo.InvariantCulture));
                current = current.parent;
            }

            segments.Reverse();
            return string.Join("/", segments);
        }

        public static string Escape(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            var builder = new StringBuilder(bytes.Length);
            foreach (var current in bytes)
            {
                var character = (char)current;
                var unreserved =
                    character >= 'a' && character <= 'z'
                    || character >= 'A' && character <= 'Z'
                    || character >= '0' && character <= '9'
                    || character == '.'
                    || character == '_'
                    || character == '-';
                if (unreserved)
                {
                    builder.Append(character);
                }
                else
                {
                    builder.Append('%');
                    builder.Append(
                        current.ToString(
                            "X2",
                            CultureInfo.InvariantCulture));
                }
            }

            return builder.ToString();
        }

        private static int GetComponentOrdinal(Component component)
        {
            var components =
                component.gameObject.GetComponents(component.GetType());
            for (var index = 0; index < components.Length; index++)
            {
                if (ReferenceEquals(components[index], component))
                {
                    return index;
                }
            }

            throw new InvalidOperationException(
                "Component is not attached to its reported GameObject.");
        }

        private static int SameNameOrdinal(Transform transform)
        {
            var parent = transform.parent;
            if (parent == null)
            {
                var roots = transform.gameObject.scene.GetRootGameObjects();
                var ordinal = 0;
                foreach (var root in roots)
                {
                    if (ReferenceEquals(root.transform, transform))
                    {
                        return ordinal;
                    }

                    if (string.Equals(
                            root.name,
                            transform.gameObject.name,
                            StringComparison.Ordinal))
                    {
                        ordinal++;
                    }
                }

                return ordinal;
            }

            var siblingOrdinal = 0;
            for (var index = 0; index < parent.childCount; index++)
            {
                var sibling = parent.GetChild(index);
                if (ReferenceEquals(sibling, transform))
                {
                    return siblingOrdinal;
                }

                if (string.Equals(
                        sibling.gameObject.name,
                        transform.gameObject.name,
                        StringComparison.Ordinal))
                {
                    siblingOrdinal++;
                }
            }

            return siblingOrdinal;
        }
    }
}
