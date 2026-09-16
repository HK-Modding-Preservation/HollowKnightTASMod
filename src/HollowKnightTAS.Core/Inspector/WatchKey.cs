using System;

namespace HollowKnightTAS.Core.Inspector
{
    public readonly struct WatchKey : IEquatable<WatchKey>
    {
        private readonly string? value;

        public WatchKey(string value, bool isVerificationStable)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty watch key is required.",
                    nameof(value));
            }

            if (value.Length > 512)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Watch keys cannot exceed 512 characters.");
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                var allowed = character >= 'a' && character <= 'z'
                              || character >= 'A' && character <= 'Z'
                              || character >= '0' && character <= '9'
                              || character == '.'
                              || character == '/'
                              || character == '_'
                              || character == '-'
                              || character == ':'
                              || character == '%';
                if (!allowed)
                {
                    throw new ArgumentException(
                        "Watch keys must use canonical ASCII path characters.",
                        nameof(value));
                }
            }

            this.value = value;
            IsVerificationStable = isVerificationStable;
        }

        public string Value => value
                               ?? throw new InvalidOperationException(
                                   "The default WatchKey is not valid.");

        public bool IsVerificationStable { get; }

        public bool Equals(WatchKey other)
        {
            return IsVerificationStable == other.IsVerificationStable
                   && string.Equals(
                       value,
                       other.value,
                       StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is WatchKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((value != null
                            ? StringComparer.Ordinal.GetHashCode(value)
                            : 0)
                        * 397)
                       ^ IsVerificationStable.GetHashCode();
            }
        }

        public override string ToString()
        {
            return Value;
        }
    }
}
