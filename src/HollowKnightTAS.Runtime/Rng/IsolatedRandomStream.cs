using System;

namespace HollowKnightTAS.Runtime.Rng
{
    internal sealed class IsolatedRandomStream<TState>
    {
        private readonly Func<TState> capture;
        private readonly Action<TState> restore;
        private TState state = default!;
        private bool initialized;
        private bool entered;

        public IsolatedRandomStream(Func<TState> capture, Action<TState> restore)
        {
            this.capture = capture;
            this.restore = restore;
        }

        public void Run(Action original)
        {
            if (entered) { original(); return; }
            var gameplay = capture();
            if (!initialized) { state = gameplay; initialized = true; }
            entered = true;
            try
            {
                restore(state);
                original();
            }
            finally
            {
                try { state = capture(); }
                finally
                {
                    try { restore(gameplay); }
                    finally { entered = false; }
                }
            }
        }
    }
}
