using System;
using HollowKnightTAS.Core.Input;
using InControl;

namespace HollowKnightTAS.Runtime.Input
{
    // Own only the normal gameplay bindings. Never synthesize committed edges
    // or alter Hero state while native menu/slot loading is in progress.
    internal sealed class NativeLifecycleInputLease : IDisposable
    {
        private readonly HeroInputAdapter adapter = new HeroInputAdapter();
        private readonly Action<string> fail;
        private HeroActions? attached;
        private bool disposed;

        public NativeLifecycleInputLease(HeroActions actions, Action<string> fail)
        {
            this.fail = fail;
            Attach(actions);
            On.InControl.PlayerActionSet.Update += OnUpdating;
        }

        private void Attach(HeroActions actions)
        {
            adapter.Attach(actions);
            attached = actions;
            adapter.Prepare(InputSample.FromHeld(0, TasAction.None, TasAction.None));
        }

        private void OnUpdating(On.InControl.PlayerActionSet.orig_Update original,
            PlayerActionSet self, ulong tick, float deltaTime)
        {
            try
            {
                if (!disposed && self is HeroActions actions
                    && ReferenceEquals(actions, InputHandler.Instance?.inputActions)
                    && !ReferenceEquals(actions, attached))
                {
                    var report = adapter.DetachAndRestore();
                    attached = null;
                    if (!report.Equivalent)
                        throw new InvalidOperationException(report.Message);
                    Attach(actions);
                }
            }
            catch (Exception exception) { fail("Native lifecycle input isolation failed: " + exception.Message); }
            finally { original(self, tick, deltaTime); }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            On.InControl.PlayerActionSet.Update -= OnUpdating;
            var report = adapter.DetachAndRestore();
            attached = null;
            if (!report.Equivalent)
                throw new InvalidOperationException("Native lifecycle bindings could not be restored: " + report.Message);
        }
    }
}
