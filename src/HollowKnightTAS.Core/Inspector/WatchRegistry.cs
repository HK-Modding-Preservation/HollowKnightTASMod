using System;
using System.Collections.Generic;
using System.Linq;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.State;

namespace HollowKnightTAS.Core.Inspector
{
    public sealed class WatchSampleContext
    {
        public WatchSampleContext(TickStamp stamp, long movieTick)
        {
            Stamp = stamp;
            MovieTick = movieTick;
        }

        public TickStamp Stamp { get; }
        public long MovieTick { get; }
    }

    public interface IWatchProvider
    {
        string ProviderId { get; }
        IEnumerable<WatchDescriptor> Describe();
        void Sample(
            WatchFrameBuilder builder,
            WatchSampleContext context);
    }

    public sealed class WatchFrameBuilder
    {
        private readonly IReadOnlyDictionary<string, WatchDescriptor>
            due;
        private readonly Dictionary<string, WatchValue> values =
            new Dictionary<string, WatchValue>(StringComparer.Ordinal);

        internal WatchFrameBuilder(
            IReadOnlyDictionary<string, WatchDescriptor> due)
        {
            this.due = due
                       ?? throw new ArgumentNullException(nameof(due));
        }

        internal IReadOnlyDictionary<string, WatchValue> Values => values;

        public bool IsDue(string key)
        {
            return key != null && due.ContainsKey(key);
        }

        public void AddBoolean(string key, bool value)
        {
            Add(key, WatchValue.FromBoolean(value));
        }

        public void AddInt32(string key, int value)
        {
            Add(key, WatchValue.FromInt32(value));
        }

        public void AddInt64(string key, long value)
        {
            Add(key, WatchValue.FromInt64(value));
        }

        public void AddFloat32(string key, float value)
        {
            Add(key, WatchValue.FromFloat32(value));
        }

        public void AddString(string key, string value)
        {
            Add(key, WatchValue.FromString(value));
        }

        public void Add(string key, WatchValue value)
        {
            if (!due.TryGetValue(key, out var descriptor))
            {
                throw new InvalidOperationException(
                    "Provider attempted to write an undeclared or non-due "
                    + "watch key: "
                    + key);
            }

            if (value.Kind != descriptor.ValueKind)
            {
                throw new InvalidOperationException(
                    "Watch key "
                    + key
                    + " requires "
                    + descriptor.ValueKind
                    + " but received "
                    + value.Kind
                    + ".");
            }

            if (values.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    "Provider wrote a watch key more than once: "
                    + key);
            }

            values.Add(key, value);
        }
    }

    public sealed class WatchRegistry : IDisposable
    {
        private readonly SortedDictionary<string, ProviderState>
            providers =
                new SortedDictionary<string, ProviderState>(
                    StringComparer.Ordinal);
        private readonly Dictionary<string, string> keyOwners =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private long sequence;
        private bool disposed;

        public int ProviderCount => providers.Count;
        public int DescriptorCount => keyOwners.Count;

        public void Register(IWatchProvider provider)
        {
            ThrowIfDisposed();
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            ValidateIdentifier(provider.ProviderId, "provider ID");
            if (providers.ContainsKey(provider.ProviderId))
            {
                throw new InvalidOperationException(
                    "Duplicate watch provider ID: "
                    + provider.ProviderId);
            }

            var descriptors = provider.Describe()?.ToList()
                              ?? throw new InvalidOperationException(
                                  "Watch provider returned null descriptors.");
            if (descriptors.Count == 0)
            {
                throw new InvalidOperationException(
                    "Watch provider must declare at least one descriptor.");
            }

            var local = new HashSet<string>(StringComparer.Ordinal);
            foreach (var descriptor in descriptors)
            {
                if (descriptor == null)
                {
                    throw new InvalidOperationException(
                        "Watch descriptor cannot be null.");
                }

                var key = descriptor.Key.Value;
                if (!local.Add(key))
                {
                    throw new InvalidOperationException(
                        "Provider declared a duplicate watch key: "
                        + key);
                }

                if (keyOwners.TryGetValue(key, out var owner))
                {
                    throw new InvalidOperationException(
                        "Watch key "
                        + key
                        + " is already owned by "
                        + owner
                        + ".");
                }
            }

            var state = new ProviderState(provider, descriptors);
            providers.Add(provider.ProviderId, state);
            foreach (var descriptor in descriptors)
            {
                keyOwners.Add(
                    descriptor.Key.Value,
                    provider.ProviderId);
            }
        }

        public bool Unregister(string providerId)
        {
            ThrowIfDisposed();
            if (!providers.TryGetValue(providerId, out var state))
            {
                return false;
            }

            providers.Remove(providerId);
            foreach (var descriptor in state.Descriptors)
            {
                keyOwners.Remove(descriptor.Key.Value);
            }

            (state.Provider as IDisposable)?.Dispose();
            return true;
        }

        public WatchFrame Sample(TickStamp stamp, long movieTick)
        {
            ThrowIfDisposed();
            var failures = new List<WatchProviderFailure>();
            foreach (var pair in providers)
            {
                var state = pair.Value;
                var due = state.GetDue(movieTick);
                if (due.Count == 0)
                {
                    continue;
                }

                var builder = new WatchFrameBuilder(due);
                try
                {
                    state.Provider.Sample(
                        builder,
                        new WatchSampleContext(stamp, movieTick));
                    var missing = due.Keys
                        .Where(key => !builder.Values.ContainsKey(key))
                        .ToArray();
                    if (missing.Length > 0)
                    {
                        throw new InvalidOperationException(
                            "Provider omitted due keys: "
                            + string.Join(",", missing));
                    }

                    state.Commit(builder.Values, stamp, movieTick);
                }
                catch (Exception exception)
                {
                    failures.Add(
                        new WatchProviderFailure(
                            state.Provider.ProviderId,
                            exception.GetType().FullName
                            + ": "
                            + exception.Message));
                }
            }

            var entries =
                new Dictionary<string, WatchFrameEntry>(
                    StringComparer.Ordinal);
            foreach (var state in providers.Values)
            {
                state.AppendEntries(entries, stamp, movieTick);
            }

            return new WatchFrame(
                checked(++sequence),
                stamp,
                movieTick,
                entries,
                failures);
        }

        public WatchFrame SampleFresh(TickStamp stamp, long movieTick)
        {
            // On-demand observations must never relabel cached values as fresh,
            // including when a provider fails at the same paused movie tick.
            InvalidateAll();
            return Sample(stamp, movieTick);
        }

        public void InvalidateAll()
        {
            ThrowIfDisposed();
            foreach (var state in providers.Values)
            {
                state.ClearCache();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            foreach (var state in providers.Values.Reverse())
            {
                (state.Provider as IDisposable)?.Dispose();
            }

            providers.Clear();
            keyOwners.Clear();
            disposed = true;
        }

        private static void ValidateIdentifier(
            string value,
            string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty " + label + " is required.");
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!(character >= 'a' && character <= 'z'
                      || character >= '0' && character <= '9'
                      || character == '.'
                      || character == '-'))
                {
                    throw new ArgumentException(
                        label
                        + " must use lowercase ASCII identifiers.");
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(WatchRegistry));
            }
        }

        private sealed class ProviderState
        {
            private readonly Dictionary<string, CachedValue> cache =
                new Dictionary<string, CachedValue>(
                    StringComparer.Ordinal);

            public ProviderState(
                IWatchProvider provider,
                IReadOnlyList<WatchDescriptor> descriptors)
            {
                Provider = provider;
                Descriptors = descriptors;
            }

            public IWatchProvider Provider { get; }
            public IReadOnlyList<WatchDescriptor> Descriptors { get; }

            public IReadOnlyDictionary<string, WatchDescriptor> GetDue(
                long movieTick)
            {
                var due =
                    new Dictionary<string, WatchDescriptor>(
                        StringComparer.Ordinal);
                foreach (var descriptor in Descriptors)
                {
                    if (!cache.TryGetValue(
                            descriptor.Key.Value,
                            out var previous)
                        || movieTick < previous.MovieTick
                        || movieTick < 0
                        || movieTick - previous.MovieTick
                        >= descriptor.SampleEveryMovieTicks)
                    {
                        due.Add(descriptor.Key.Value, descriptor);
                    }
                }

                return due;
            }

            public void Commit(
                IReadOnlyDictionary<string, WatchValue> values,
                TickStamp stamp,
                long movieTick)
            {
                foreach (var pair in values)
                {
                    var descriptor = Descriptors.First(
                        value => string.Equals(
                            value.Key.Value,
                            pair.Key,
                            StringComparison.Ordinal));
                    cache[pair.Key] = new CachedValue(
                        descriptor,
                        pair.Value,
                        stamp,
                        movieTick);
                }
            }

            public void AppendEntries(
                IDictionary<string, WatchFrameEntry> target,
                TickStamp currentStamp,
                long currentMovieTick)
            {
                foreach (var pair in cache)
                {
                    var cached = pair.Value;
                    var fresh =
                        cached.Stamp.Equals(currentStamp)
                        && cached.MovieTick == currentMovieTick;
                    var age = currentMovieTick >= cached.MovieTick
                        ? currentMovieTick - cached.MovieTick
                        : 0;
                    target.Add(
                        pair.Key,
                        new WatchFrameEntry(
                            cached.Descriptor,
                            cached.Value,
                            cached.Stamp,
                            cached.MovieTick,
                            fresh,
                            age));
                }
            }

            public void ClearCache()
            {
                cache.Clear();
            }
        }

        private sealed class CachedValue
        {
            public CachedValue(
                WatchDescriptor descriptor,
                WatchValue value,
                TickStamp stamp,
                long movieTick)
            {
                Descriptor = descriptor;
                Value = value;
                Stamp = stamp;
                MovieTick = movieTick;
            }

            public WatchDescriptor Descriptor { get; }
            public WatchValue Value { get; }
            public TickStamp Stamp { get; }
            public long MovieTick { get; }
        }
    }
}
