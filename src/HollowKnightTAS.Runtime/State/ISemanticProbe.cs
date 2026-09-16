using HollowKnightTAS.Core.State;

namespace HollowKnightTAS.Runtime.State
{
    public interface ISemanticProbe
    {
        string ProbeId { get; }
        void Capture(SemanticSnapshotBuilder builder);
    }
}
