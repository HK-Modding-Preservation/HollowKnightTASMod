using System;

namespace HollowKnightTAS.Core.Movie
{
    public sealed class AssertCommand : MovieCommand
    {
        public AssertCommand(
            string semanticPath,
            string @operator,
            string value,
            bool valueIsQuoted,
            MovieSourceSpan span)
            : base(span)
        {
            SemanticPath = semanticPath
                           ?? throw new ArgumentNullException(nameof(semanticPath));
            Operator = @operator ?? throw new ArgumentNullException(nameof(@operator));
            Value = value ?? throw new ArgumentNullException(nameof(value));
            ValueIsQuoted = valueIsQuoted;
        }

        public string SemanticPath { get; }
        public string Operator { get; }
        public string Value { get; }
        public bool ValueIsQuoted { get; }
    }
}
