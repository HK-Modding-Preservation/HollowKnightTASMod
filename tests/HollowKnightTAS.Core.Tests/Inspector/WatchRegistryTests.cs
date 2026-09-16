using System;
using System.Collections.Generic;
using System.Linq;
using HollowKnightTAS.Core.Inspector;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.State;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Inspector
{
    [TestClass]
    public sealed class WatchRegistryTests
    {
        [TestMethod]
        public void SampleFresh_RefreshesSamePausedTickAndDoesNotReuseFailedValues()
        {
            using var registry = new WatchRegistry();
            var value = 5;
            var fail = false;
            registry.Register(new DelegateProvider("provider.live", Descriptor("hero.health"),
                (builder, _) =>
                {
                    if (fail) throw new InvalidOperationException("read failed");
                    builder.AddInt32("hero.health", value);
                }));
            var first = registry.SampleFresh(Stamp(10), 9);
            value = 4;
            var second = registry.SampleFresh(Stamp(10), 9);
            Assert.AreEqual("5", first.Entries["hero.health"].Value.DisplayValue);
            Assert.AreEqual("4", second.Entries["hero.health"].Value.DisplayValue);
            Assert.IsTrue(second.Entries["hero.health"].IsFresh);
            Assert.AreEqual(9L, second.MovieTick);
            fail = true;
            var failed = registry.SampleFresh(Stamp(10), 9);
            Assert.AreEqual(0, failed.Entries.Count);
            Assert.AreEqual(1, failed.Failures.Count);
        }

        [TestMethod]
        public void Register_RejectsDuplicateProviderAndKey()
        {
            using var registry = new WatchRegistry();
            registry.Register(
                new DelegateProvider(
                    "provider.one",
                    Descriptor("hero.health"),
                    (builder, _) =>
                        builder.AddInt32("hero.health", 5)));

            Assert.ThrowsExactly<InvalidOperationException>(
                () => registry.Register(
                    new DelegateProvider(
                        "provider.one",
                        Descriptor("hero.soul"),
                        (builder, _) =>
                            builder.AddInt32("hero.soul", 3))));
            Assert.ThrowsExactly<InvalidOperationException>(
                () => registry.Register(
                    new DelegateProvider(
                        "provider.two",
                        Descriptor("hero.health"),
                        (builder, _) =>
                            builder.AddInt32("hero.health", 5))));
        }

        [TestMethod]
        public void SamplingCadence_CarriesExplicitlyStaleValue()
        {
            using var registry = new WatchRegistry();
            var provider = new CadenceProvider();
            registry.Register(provider);

            var first = registry.Sample(Stamp(1), 0);
            var second = registry.Sample(Stamp(2), 1);
            var fourth = registry.Sample(Stamp(4), 3);

            Assert.AreEqual(2, first.Entries.Count);
            Assert.IsTrue(first.Entries["test.fast"].IsFresh);
            Assert.IsTrue(first.Entries["test.slow"].IsFresh);
            Assert.AreEqual("1", second.Entries["test.fast"].Value.DisplayValue);
            Assert.IsTrue(second.Entries["test.fast"].IsFresh);
            Assert.AreEqual("0", second.Entries["test.slow"].Value.DisplayValue);
            Assert.IsFalse(second.Entries["test.slow"].IsFresh);
            Assert.AreEqual(1, second.Entries["test.slow"].AgeMovieTicks);
            Assert.AreEqual("3", fourth.Entries["test.slow"].Value.DisplayValue);
            Assert.AreEqual(3, provider.SampleCount);
        }

        [TestMethod]
        public void ReadFailure_IsReportedAndDoesNotInventDefault()
        {
            using var registry = new WatchRegistry();
            registry.Register(
                new DelegateProvider(
                    "provider.fail",
                    Descriptor("hero.health"),
                    (_, __) => throw new InvalidOperationException("missing")));

            var frame = registry.Sample(Stamp(1), 0);

            Assert.AreEqual(0, frame.Entries.Count);
            Assert.AreEqual(1, frame.Failures.Count);
            Assert.AreEqual(
                "provider.fail",
                frame.Failures[0].ProviderId);
            StringAssert.Contains(frame.Failures[0].Error, "missing");
        }

        [TestMethod]
        public void TypeMismatch_IsFailureRatherThanCoercion()
        {
            using var registry = new WatchRegistry();
            registry.Register(
                new DelegateProvider(
                    "provider.type",
                    Descriptor("hero.health"),
                    (builder, _) =>
                        builder.AddString("hero.health", "5")));

            var frame = registry.Sample(Stamp(1), 0);

            Assert.AreEqual(0, frame.Entries.Count);
            Assert.AreEqual(1, frame.Failures.Count);
            StringAssert.Contains(
                frame.Failures[0].Error,
                "requires Int32");
        }

        [TestMethod]
        public void UndeclaredOrOmittedKey_IsFailure()
        {
            using var undeclared = new WatchRegistry();
            undeclared.Register(
                new DelegateProvider(
                    "provider.extra",
                    Descriptor("hero.health"),
                    (builder, _) =>
                        builder.AddInt32("hero.soul", 3)));
            Assert.AreEqual(
                1,
                undeclared.Sample(Stamp(1), 0).Failures.Count);

            using var omitted = new WatchRegistry();
            omitted.Register(
                new DelegateProvider(
                    "provider.omit",
                    Descriptor("hero.health"),
                    (_, __) => { }));
            var omittedFrame = omitted.Sample(Stamp(1), 0);
            Assert.AreEqual(0, omittedFrame.Entries.Count);
            StringAssert.Contains(
                omittedFrame.Failures[0].Error,
                "omitted due keys");
        }

        [TestMethod]
        public void Unregister_DisposesProviderAndRemovesValues()
        {
            using var registry = new WatchRegistry();
            var provider = new DisposableProvider();
            registry.Register(provider);
            Assert.AreEqual(1, registry.Sample(Stamp(1), 0).Entries.Count);

            Assert.IsTrue(registry.Unregister(provider.ProviderId));
            Assert.IsTrue(provider.Disposed);
            Assert.AreEqual(0, registry.Sample(Stamp(2), 1).Entries.Count);
            Assert.IsFalse(registry.Unregister(provider.ProviderId));
        }

        [TestMethod]
        public void InvalidateAll_ForcesSameMovieTickResample()
        {
            using var registry = new WatchRegistry();
            var provider = new CadenceProvider();
            registry.Register(provider);
            var first = registry.Sample(Stamp(1), 10);
            var cached = registry.Sample(Stamp(2), 10);
            Assert.AreEqual(1, provider.SampleCount);

            registry.InvalidateAll();
            var rebuilt = registry.Sample(Stamp(3), 10);

            Assert.IsFalse(cached.Entries["test.fast"].IsFresh);
            Assert.IsTrue(rebuilt.Entries["test.fast"].IsFresh);
            Assert.AreEqual(2, provider.SampleCount);
            CollectionAssert.AreEqual(
                first.Entries.Keys.OrderBy(value => value).ToArray(),
                rebuilt.Entries.Keys.OrderBy(value => value).ToArray());
        }

        [TestMethod]
        public void WatchFrameJson_IsCanonicalAndRetainsTypeAndStability()
        {
            using var registry = new WatchRegistry();
            registry.Register(
                new DelegateProvider(
                    "provider.one",
                    Descriptor("hero.health"),
                    (builder, _) =>
                        builder.AddInt32("hero.health", 5)));
            var frame = registry.Sample(Stamp(1), 0);

            var left = WatchFrameJson.Serialize(frame);
            var right = WatchFrameJson.Serialize(frame);

            Assert.AreEqual(left, right);
            StringAssert.Contains(left, "\"kind\":\"Int32\"");
            StringAssert.Contains(
                left,
                "\"verificationStable\":true");
            StringAssert.Contains(left, "\"canonicalHex\":\"00000005\"");
        }

        [TestMethod]
        public void VerificationSchema_RegistersT04T05KeysButNotDisplayOnly()
        {
            foreach (var pair in SemanticSnapshotSchemaV1.Kinds)
            {
                var descriptor = new WatchDescriptor(
                    new WatchKey(pair.Key, true),
                    pair.Value,
                    "semantic",
                    1,
                    pair.Key);
                Assert.IsTrue(
                    InspectorVerificationWatchSchemaV1.IsRegistered(
                        descriptor),
                    pair.Key);
            }

            var displayOnly = new WatchDescriptor(
                new WatchKey("display/scene/1/enemy/hp", false),
                SemanticValueKind.Int32,
                "enemy",
                1,
                "HP");
            Assert.IsFalse(
                InspectorVerificationWatchSchemaV1.IsRegistered(
                    displayOnly));
        }

        [TestMethod]
        [DataRow("hero.control.acceptingInput", SemanticValueKind.Boolean)]
        [DataRow("hero.control.relinquished", SemanticValueKind.Boolean)]
        [DataRow("hero.animation.controlEnabled", SemanticValueKind.Boolean)]
        [DataRow("hero.animation.clip", SemanticValueKind.Utf8String)]
        [DataRow("hero.animation.frame", SemanticValueKind.Int32)]
        [DataRow("hero.cState.transitioning", SemanticValueKind.Boolean)]
        public void VerificationSchema_RegistersHeroControlAndAnimationKeys(
            string key,
            SemanticValueKind kind)
        {
            var descriptor = new WatchDescriptor(
                new WatchKey(key, true),
                kind,
                "hero",
                1,
                key);

            Assert.IsTrue(
                InspectorVerificationWatchSchemaV1.IsRegistered(
                    descriptor));
        }

        [TestMethod]
        public void WatchKey_RejectsNonCanonicalOrDefaultValues()
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new WatchKey("enemy path", true));
            Assert.ThrowsExactly<InvalidOperationException>(
                () => _ = default(WatchKey).Value);
        }

        private static WatchDescriptor Descriptor(string key)
        {
            return new WatchDescriptor(
                new WatchKey(key, true),
                SemanticValueKind.Int32,
                "hero",
                1,
                key);
        }

        private static TickStamp Stamp(long value)
        {
            return new TickStamp(
                checked((ulong)value),
                value,
                value,
                0,
                TickPhase.LateUpdateEnd);
        }

        private sealed class DelegateProvider : IWatchProvider
        {
            private readonly IReadOnlyList<WatchDescriptor> descriptors;
            private readonly Action<WatchFrameBuilder, WatchSampleContext>
                sample;

            public DelegateProvider(
                string providerId,
                WatchDescriptor descriptor,
                Action<WatchFrameBuilder, WatchSampleContext> sample)
            {
                ProviderId = providerId;
                descriptors = new[] { descriptor };
                this.sample = sample;
            }

            public string ProviderId { get; }

            public IEnumerable<WatchDescriptor> Describe()
            {
                return descriptors;
            }

            public void Sample(
                WatchFrameBuilder builder,
                WatchSampleContext context)
            {
                sample(builder, context);
            }
        }

        private sealed class CadenceProvider : IWatchProvider
        {
            private readonly WatchDescriptor[] descriptors =
            {
                new WatchDescriptor(
                    new WatchKey("test.fast", true),
                    SemanticValueKind.Int64,
                    "test",
                    1,
                    "Fast"),
                new WatchDescriptor(
                    new WatchKey("test.slow", true),
                    SemanticValueKind.Int64,
                    "test",
                    3,
                    "Slow")
            };

            public string ProviderId => "provider.cadence";
            public int SampleCount { get; private set; }

            public IEnumerable<WatchDescriptor> Describe()
            {
                return descriptors;
            }

            public void Sample(
                WatchFrameBuilder builder,
                WatchSampleContext context)
            {
                SampleCount++;
                if (builder.IsDue("test.fast"))
                {
                    builder.AddInt64(
                        "test.fast",
                        context.MovieTick);
                }

                if (builder.IsDue("test.slow"))
                {
                    builder.AddInt64(
                        "test.slow",
                        context.MovieTick);
                }
            }
        }

        private sealed class DisposableProvider :
            IWatchProvider,
            IDisposable
        {
            public string ProviderId => "provider.disposable";
            public bool Disposed { get; private set; }

            public IEnumerable<WatchDescriptor> Describe()
            {
                yield return Descriptor("hero.health");
            }

            public void Sample(
                WatchFrameBuilder builder,
                WatchSampleContext context)
            {
                builder.AddInt32("hero.health", 5);
            }

            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
