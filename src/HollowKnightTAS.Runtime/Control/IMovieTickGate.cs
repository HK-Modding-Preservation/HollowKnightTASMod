using HollowKnightTAS.Core.Ledger;

namespace HollowKnightTAS.Runtime.Control
{
    public interface IMovieTickGate
    {
        bool TryAuthorizeMovieTick(ulong rawInputTick);

        void OnMovieTickSkipped(ulong rawInputTick);

        void OnMovieTickCommitted(
            long movieTick,
            TickStamp stamp);
    }

    public interface ITransitionReleaseMovieTickGate
    {
        bool TryAuthorizeTransitionRelease(ulong rawInputTick);
    }
}
