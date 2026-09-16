using HollowKnightTAS.Core.Input;

namespace HollowKnightTAS.Core.Movie
{
    public sealed class FrameRunCommand : MovieCommand
    {
        public FrameRunCommand(
            long frameCount,
            TasAction heldActions,
            int axisX,
            int axisY,
            bool hasAnalogAxes,
            MovieSourceSpan span)
            : base(span)
        {
            FrameCount = frameCount;
            HeldActions = heldActions;
            AxisX = axisX;
            AxisY = axisY;
            HasAnalogAxes = hasAnalogAxes;
        }

        public long FrameCount { get; }
        public TasAction HeldActions { get; }
        public int AxisX { get; }
        public int AxisY { get; }
        public bool HasAnalogAxes { get; }

        public bool HasSameInput(FrameRunCommand other)
        {
            return other != null
                   && HeldActions == other.HeldActions
                   && AxisX == other.AxisX
                   && AxisY == other.AxisY;
        }
    }
}
