using System;
using System.Collections.Generic;
using System.Globalization;
using HollowKnightTAS.Core.Inspector;
using HollowKnightTAS.Core.State;
using UnityEngine;

namespace HollowKnightTAS.Runtime.Inspector
{
    public sealed class ColliderWatchProvider : IWatchProvider
    {
        private readonly Collider2D collider;
        private readonly WatchDescriptor[] descriptors;
        private readonly string enabledKey;
        private readonly string triggerKey;
        private readonly string centerXKey;
        private readonly string centerYKey;
        private readonly string extentsXKey;
        private readonly string extentsYKey;
        private readonly string shapeKey;

        public ColliderWatchProvider(
            string providerId,
            Collider2D collider,
            string keyPrefix,
            bool verificationStable,
            int sampleEveryMovieTicks)
        {
            ProviderId = providerId
                         ?? throw new ArgumentNullException(
                             nameof(providerId));
            this.collider = collider
                            ?? throw new ArgumentNullException(
                                nameof(collider));
            if (string.IsNullOrWhiteSpace(keyPrefix))
            {
                throw new ArgumentException(
                    "A key prefix is required.",
                    nameof(keyPrefix));
            }

            KeyPrefix = keyPrefix;
            enabledKey = keyPrefix + "/enabled";
            triggerKey = keyPrefix + "/isTrigger";
            centerXKey = keyPrefix + "/bounds.center.x";
            centerYKey = keyPrefix + "/bounds.center.y";
            extentsXKey = keyPrefix + "/bounds.extents.x";
            extentsYKey = keyPrefix + "/bounds.extents.y";
            shapeKey = keyPrefix + "/shape";
            descriptors = new[]
            {
                Descriptor(
                    enabledKey,
                    verificationStable,
                    SemanticValueKind.Boolean,
                    "Enabled",
                    sampleEveryMovieTicks),
                Descriptor(
                    triggerKey,
                    verificationStable,
                    SemanticValueKind.Boolean,
                    "Trigger",
                    sampleEveryMovieTicks),
                Descriptor(
                    centerXKey,
                    verificationStable,
                    SemanticValueKind.Float32Bits,
                    "Bounds center X",
                    sampleEveryMovieTicks),
                Descriptor(
                    centerYKey,
                    verificationStable,
                    SemanticValueKind.Float32Bits,
                    "Bounds center Y",
                    sampleEveryMovieTicks),
                Descriptor(
                    extentsXKey,
                    verificationStable,
                    SemanticValueKind.Float32Bits,
                    "Bounds extents X",
                    sampleEveryMovieTicks),
                Descriptor(
                    extentsYKey,
                    verificationStable,
                    SemanticValueKind.Float32Bits,
                    "Bounds extents Y",
                    sampleEveryMovieTicks),
                Descriptor(
                    shapeKey,
                    verificationStable,
                    SemanticValueKind.Utf8String,
                    "Shape",
                    sampleEveryMovieTicks)
            };
        }

        public string ProviderId { get; }
        public string KeyPrefix { get; }
        public Collider2D Target => collider;
        public bool IsAlive => collider != null;

        public IEnumerable<WatchDescriptor> Describe()
        {
            return descriptors;
        }

        public void Sample(
            WatchFrameBuilder builder,
            WatchSampleContext context)
        {
            if (!IsAlive)
            {
                throw new InvalidOperationException(
                    "The registered Collider2D was destroyed.");
            }

            var bounds = collider.bounds;
            builder.AddBoolean(enabledKey, collider.enabled);
            builder.AddBoolean(triggerKey, collider.isTrigger);
            builder.AddFloat32(centerXKey, bounds.center.x);
            builder.AddFloat32(centerYKey, bounds.center.y);
            builder.AddFloat32(extentsXKey, bounds.extents.x);
            builder.AddFloat32(extentsYKey, bounds.extents.y);
            builder.AddString(shapeKey, DescribeShape(collider));
        }

        private static string DescribeShape(Collider2D value)
        {
            if (value is BoxCollider2D box)
            {
                return "Box:size="
                       + Format(box.size.x)
                       + ","
                       + Format(box.size.y)
                       + ";offset="
                       + Format(box.offset.x)
                       + ","
                       + Format(box.offset.y);
            }

            if (value is CircleCollider2D circle)
            {
                return "Circle:radius="
                       + Format(circle.radius)
                       + ";offset="
                       + Format(circle.offset.x)
                       + ","
                       + Format(circle.offset.y);
            }

            if (value is CapsuleCollider2D capsule)
            {
                return "Capsule:size="
                       + Format(capsule.size.x)
                       + ","
                       + Format(capsule.size.y)
                       + ";direction="
                       + capsule.direction;
            }

            if (value is PolygonCollider2D polygon)
            {
                return "Polygon:pathCount="
                       + polygon.pathCount.ToString(
                           CultureInfo.InvariantCulture);
            }

            return value.GetType().Name;
        }

        private static string Format(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static WatchDescriptor Descriptor(
            string key,
            bool stable,
            SemanticValueKind kind,
            string label,
            int interval)
        {
            return new WatchDescriptor(
                new WatchKey(key, stable),
                kind,
                "collider",
                interval,
                label);
        }
    }
}
