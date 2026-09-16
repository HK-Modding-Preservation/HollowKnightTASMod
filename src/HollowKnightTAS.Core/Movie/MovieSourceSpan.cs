using System;

namespace HollowKnightTAS.Core.Movie
{
    public readonly struct MovieSourceSpan : IEquatable<MovieSourceSpan>
    {
        public MovieSourceSpan(
            string source,
            int line,
            int column,
            int length)
        {
            Source = string.IsNullOrWhiteSpace(source) ? "<movie>" : source;
            Line = line > 0 ? line : 1;
            Column = column > 0 ? column : 1;
            Length = length > 0 ? length : 1;
        }

        public string Source { get; }
        public int Line { get; }
        public int Column { get; }
        public int Length { get; }

        public bool Equals(MovieSourceSpan other)
        {
            return string.Equals(Source, other.Source, StringComparison.Ordinal)
                   && Line == other.Line
                   && Column == other.Column
                   && Length == other.Length;
        }

        public override bool Equals(object? value)
        {
            return value is MovieSourceSpan other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = StringComparer.Ordinal.GetHashCode(Source);
                hash = (hash * 397) ^ Line;
                hash = (hash * 397) ^ Column;
                hash = (hash * 397) ^ Length;
                return hash;
            }
        }
    }
}
