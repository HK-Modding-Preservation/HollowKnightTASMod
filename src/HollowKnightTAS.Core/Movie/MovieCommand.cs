namespace HollowKnightTAS.Core.Movie
{
    public abstract class MovieCommand
    {
        protected MovieCommand(MovieSourceSpan span)
        {
            Span = span;
        }

        public MovieSourceSpan Span { get; }
    }
}
