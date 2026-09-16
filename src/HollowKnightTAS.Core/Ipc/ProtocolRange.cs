using System;

namespace HollowKnightTAS.Core.Ipc
{
    public readonly struct ProtocolRange : IEquatable<ProtocolRange>
    {
        public ProtocolRange(int minimum, int maximum)
        {
            if (minimum <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimum),
                    "Protocol versions must be positive.");
            }

            if (maximum < minimum)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximum),
                    "Maximum protocol must be at least the minimum.");
            }

            Minimum = minimum;
            Maximum = maximum;
        }

        public int Minimum { get; }
        public int Maximum { get; }

        public bool Intersects(ProtocolRange other)
        {
            return Math.Max(Minimum, other.Minimum)
                   <= Math.Min(Maximum, other.Maximum);
        }

        public bool TryNegotiate(
            ProtocolRange other,
            out int protocolVersion)
        {
            protocolVersion = Math.Min(Maximum, other.Maximum);
            return protocolVersion >= Math.Max(Minimum, other.Minimum);
        }

        public bool Contains(int version)
        {
            return version >= Minimum && version <= Maximum;
        }

        public bool Equals(ProtocolRange other)
        {
            return Minimum == other.Minimum
                   && Maximum == other.Maximum;
        }

        public override bool Equals(object? obj)
        {
            return obj is ProtocolRange other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Minimum * 397) ^ Maximum;
            }
        }

        public override string ToString()
        {
            return Minimum == Maximum
                ? Minimum.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : Minimum.ToString(System.Globalization.CultureInfo.InvariantCulture)
                  + "-"
                  + Maximum.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
