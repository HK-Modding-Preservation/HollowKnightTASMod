using System;

namespace HollowKnightTAS.Core.Movie
{
    public sealed class CheckpointCommand : MovieCommand
    {
        public CheckpointCommand(string identifier, MovieSourceSpan span)
            : base(span)
        {
            Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));
        }

        public string Identifier { get; }
    }
}
