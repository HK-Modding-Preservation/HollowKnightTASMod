using System;

namespace HollowKnightTAS.Core.Ledger
{
    public readonly struct TickStamp : IEquatable<TickStamp>
    {
        public TickStamp(
            ulong inputTick,
            long visualTick,
            long fixedTick,
            int sceneEpoch,
            TickPhase phase)
        {
            if (visualTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(visualTick));
            }

            if (fixedTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedTick));
            }

            if (sceneEpoch < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sceneEpoch));
            }

            if (!Enum.IsDefined(typeof(TickPhase), phase))
            {
                throw new ArgumentOutOfRangeException(nameof(phase));
            }

            InputTick = inputTick;
            VisualTick = visualTick;
            FixedTick = fixedTick;
            SceneEpoch = sceneEpoch;
            Phase = phase;
        }

        public ulong InputTick { get; }
        public long VisualTick { get; }
        public long FixedTick { get; }
        public int SceneEpoch { get; }
        public TickPhase Phase { get; }

        public bool Equals(TickStamp other)
        {
            return InputTick == other.InputTick
                   && VisualTick == other.VisualTick
                   && FixedTick == other.FixedTick
                   && SceneEpoch == other.SceneEpoch
                   && Phase == other.Phase;
        }

        public override bool Equals(object? value)
        {
            return value is TickStamp other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = InputTick.GetHashCode();
                hash = (hash * 397) ^ VisualTick.GetHashCode();
                hash = (hash * 397) ^ FixedTick.GetHashCode();
                hash = (hash * 397) ^ SceneEpoch;
                hash = (hash * 397) ^ (int)Phase;
                return hash;
            }
        }
    }
}
