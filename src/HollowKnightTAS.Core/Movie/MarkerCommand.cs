using System;

namespace HollowKnightTAS.Core.Movie
{
    public sealed class MarkerCommand : MovieCommand
    {
        public MarkerCommand(string text, MovieSourceSpan span)
            : base(span)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }

        public string Text { get; }
    }
}
