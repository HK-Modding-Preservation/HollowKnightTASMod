using System;

namespace HollowKnightTAS.Core.Control
{
    public readonly struct StepRequest : IEquatable<StepRequest>
    {
        public StepRequest(StepBoundary boundary, int count)
        {
            if (!Enum.IsDefined(typeof(StepBoundary), boundary))
            {
                throw new ArgumentOutOfRangeException(nameof(boundary));
            }

            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    "Step count must be positive.");
            }

            Boundary = boundary;
            Count = count;
        }

        public StepBoundary Boundary { get; }
        public int Count { get; }

        public bool Equals(StepRequest other)
        {
            return Boundary == other.Boundary && Count == other.Count;
        }

        public override bool Equals(object? value)
        {
            return value is StepRequest other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Boundary * 397) ^ Count;
            }
        }
    }
}
