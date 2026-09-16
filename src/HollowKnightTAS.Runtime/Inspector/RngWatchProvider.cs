using System;
using System.Collections.Generic;
using HollowKnightTAS.Core.Inspector;
using HollowKnightTAS.Core.State;
using HollowKnightTAS.Runtime.Rng;

namespace HollowKnightTAS.Runtime.Inspector
{
    public sealed class RngWatchProvider : IWatchProvider
    {
        private readonly RuntimeRngProbe probe;
        private readonly WatchDescriptor[] descriptors;

        public RngWatchProvider(
            RuntimeRngProbe probe,
            int sampleEveryMovieTicks)
        {
            this.probe = probe
                         ?? throw new ArgumentNullException(nameof(probe));
            descriptors = new[]
            {
                new WatchDescriptor(
                    new WatchKey("rng.state.sha256", true),
                    SemanticValueKind.Utf8String,
                    "rng",
                    sampleEveryMovieTicks,
                    "Unity RNG state"),
                new WatchDescriptor(
                    new WatchKey("rng.callCount", true),
                    SemanticValueKind.Int64,
                    "rng",
                    sampleEveryMovieTicks,
                    "Whitelisted calls")
            };
        }

        public string ProviderId => "default.rng";

        public IEnumerable<WatchDescriptor> Describe()
        {
            return descriptors;
        }

        public void Sample(
            WatchFrameBuilder builder,
            WatchSampleContext context)
        {
            if (!probe.StateAvailable)
            {
                throw new InvalidOperationException(
                    "T10 RNG state is unavailable.");
            }

            builder.AddString(
                "rng.state.sha256",
                probe.CaptureCurrent().Sha256);
            builder.AddInt64(
                "rng.callCount",
                probe.GlobalCallCount);
        }
    }
}
