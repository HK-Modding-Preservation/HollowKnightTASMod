using System;
using HollowKnightTAS.Core.State;

namespace HollowKnightTAS.Core.Inspector
{
    public readonly struct WatchValue : IEquatable<WatchValue>
    {
        private readonly SemanticValue value;
        private readonly bool initialized;

        private WatchValue(SemanticValue value)
        {
            this.value = value;
            initialized = true;
        }

        public SemanticValueKind Kind => RequireValue().Kind;
        public string DisplayValue => RequireValue().DisplayValue;
        public string CanonicalHex => RequireValue().CanonicalHex;
        public byte[] CanonicalBytes => RequireValue().GetCanonicalBytes();

        public static WatchValue FromBoolean(bool value)
        {
            return new WatchValue(SemanticValue.FromBoolean(value));
        }

        public static WatchValue FromInt32(int value)
        {
            return new WatchValue(SemanticValue.FromInt32(value));
        }

        public static WatchValue FromInt64(long value)
        {
            return new WatchValue(SemanticValue.FromInt64(value));
        }

        public static WatchValue FromFloat32(float value)
        {
            return new WatchValue(SemanticValue.FromFloat32(value));
        }

        public static WatchValue FromFloat32Bits(int bits)
        {
            return new WatchValue(SemanticValue.FromFloat32Bits(bits));
        }

        public static WatchValue FromString(string value)
        {
            return new WatchValue(SemanticValue.FromString(value));
        }

        public bool Equals(WatchValue other)
        {
            return initialized == other.initialized
                   && (!initialized || value.Equals(other.value));
        }

        public override bool Equals(object? obj)
        {
            return obj is WatchValue other && Equals(other);
        }

        public override int GetHashCode()
        {
            return initialized ? value.GetHashCode() : 0;
        }

        private SemanticValue RequireValue()
        {
            if (!initialized)
            {
                throw new InvalidOperationException(
                    "The default WatchValue is not valid.");
            }

            return value;
        }
    }
}
