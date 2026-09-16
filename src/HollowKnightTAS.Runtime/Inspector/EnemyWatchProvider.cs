using System;
using System.Collections.Generic;
using HollowKnightTAS.Core.Inspector;
using HollowKnightTAS.Core.State;
using UnityEngine;

namespace HollowKnightTAS.Runtime.Inspector
{
    public sealed class EnemyWatchProvider : IWatchProvider
    {
        private readonly HealthManager health;
        private readonly Rigidbody2D body;
        private readonly WatchDescriptor[] descriptors;
        private readonly string hpKey;
        private readonly string deadKey;
        private readonly string positionXKey;
        private readonly string positionYKey;
        private readonly string velocityXKey;
        private readonly string velocityYKey;

        public EnemyWatchProvider(
            string providerId,
            HealthManager health,
            Rigidbody2D body,
            string keyPrefix,
            bool verificationStable,
            int sampleEveryMovieTicks)
        {
            ProviderId = providerId
                         ?? throw new ArgumentNullException(
                             nameof(providerId));
            this.health = health
                          ?? throw new ArgumentNullException(
                              nameof(health));
            this.body = body
                        ?? throw new ArgumentNullException(nameof(body));
            if (string.IsNullOrWhiteSpace(keyPrefix))
            {
                throw new ArgumentException(
                    "A key prefix is required.",
                    nameof(keyPrefix));
            }

            KeyPrefix = keyPrefix;
            hpKey = keyPrefix + "/hp";
            deadKey = keyPrefix + "/dead";
            positionXKey = keyPrefix + "/position.x";
            positionYKey = keyPrefix + "/position.y";
            velocityXKey = keyPrefix + "/velocity.x";
            velocityYKey = keyPrefix + "/velocity.y";
            descriptors = new[]
            {
                Descriptor(
                    hpKey,
                    verificationStable,
                    SemanticValueKind.Int32,
                    "HP",
                    sampleEveryMovieTicks),
                Descriptor(
                    deadKey,
                    verificationStable,
                    SemanticValueKind.Boolean,
                    "Dead",
                    sampleEveryMovieTicks),
                Descriptor(
                    positionXKey,
                    verificationStable,
                    SemanticValueKind.Float32Bits,
                    "Position X",
                    sampleEveryMovieTicks),
                Descriptor(
                    positionYKey,
                    verificationStable,
                    SemanticValueKind.Float32Bits,
                    "Position Y",
                    sampleEveryMovieTicks),
                Descriptor(
                    velocityXKey,
                    verificationStable,
                    SemanticValueKind.Float32Bits,
                    "Velocity X",
                    sampleEveryMovieTicks),
                Descriptor(
                    velocityYKey,
                    verificationStable,
                    SemanticValueKind.Float32Bits,
                    "Velocity Y",
                    sampleEveryMovieTicks)
            };
        }

        public string ProviderId { get; }
        public string KeyPrefix { get; }
        public HealthManager Target => health;
        public bool IsAlive => health != null && body != null;

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
                    "The registered enemy was destroyed.");
            }

            var position = body.position;
            var velocity = body.velocity;
            builder.AddInt32(hpKey, health.hp);
            builder.AddBoolean(deadKey, health.isDead);
            builder.AddFloat32(positionXKey, position.x);
            builder.AddFloat32(positionYKey, position.y);
            builder.AddFloat32(velocityXKey, velocity.x);
            builder.AddFloat32(velocityYKey, velocity.y);
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
                "enemy",
                interval,
                label);
        }
    }
}
