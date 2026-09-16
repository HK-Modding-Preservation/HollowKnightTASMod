using System;
using System.Collections.Generic;
using HollowKnightTAS.Core.Inspector;
using HollowKnightTAS.Core.State;

namespace HollowKnightTAS.Runtime.Inspector
{
    public sealed class FsmWatchProvider :
        IWatchProvider,
        IDisposable
    {
        private readonly PlayMakerFSM fsm;
        private readonly WatchDescriptor[] descriptors;
        private readonly string activeKey;
        private readonly string stateKey;
        private readonly string eventKey;
        private string recentEvent = "none";
        private bool disposed;

        public FsmWatchProvider(
            string providerId,
            PlayMakerFSM fsm,
            string keyPrefix,
            bool verificationStable,
            int sampleEveryMovieTicks)
        {
            ProviderId = providerId
                         ?? throw new ArgumentNullException(
                             nameof(providerId));
            this.fsm = fsm
                       ?? throw new ArgumentNullException(nameof(fsm));
            if (string.IsNullOrWhiteSpace(keyPrefix))
            {
                throw new ArgumentException(
                    "A key prefix is required.",
                    nameof(keyPrefix));
            }

            KeyPrefix = keyPrefix;
            activeKey = keyPrefix + "/active";
            stateKey = keyPrefix + "/activeState";
            eventKey = keyPrefix + "/recentEvent";
            descriptors = new[]
            {
                new WatchDescriptor(
                    new WatchKey(activeKey, verificationStable),
                    SemanticValueKind.Boolean,
                    "fsm",
                    sampleEveryMovieTicks,
                    "FSM active"),
                new WatchDescriptor(
                    new WatchKey(stateKey, verificationStable),
                    SemanticValueKind.Utf8String,
                    "fsm",
                    sampleEveryMovieTicks,
                    "FSM state"),
                new WatchDescriptor(
                    new WatchKey(eventKey, verificationStable),
                    SemanticValueKind.Utf8String,
                    "fsm",
                    sampleEveryMovieTicks,
                    "Recent event")
            };
            On.PlayMakerFSM.SendEvent += OnSendEvent;
        }

        public string ProviderId { get; }
        public string KeyPrefix { get; }
        public PlayMakerFSM Target => fsm;
        public bool IsAlive => !disposed && fsm != null;

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
                    "The registered PlayMakerFSM was destroyed.");
            }

            builder.AddBoolean(activeKey, fsm.Active);
            builder.AddString(
                stateKey,
                fsm.ActiveStateName ?? string.Empty);
            builder.AddString(eventKey, recentEvent);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            On.PlayMakerFSM.SendEvent -= OnSendEvent;
        }

        private void OnSendEvent(
            On.PlayMakerFSM.orig_SendEvent original,
            PlayMakerFSM self,
            string eventName)
        {
            if (!disposed && ReferenceEquals(self, fsm))
            {
                recentEvent = eventName ?? string.Empty;
            }

            original(self, eventName);
        }
    }
}
