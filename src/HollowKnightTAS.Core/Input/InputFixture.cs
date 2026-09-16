using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HollowKnightTAS.Core.Input
{
    public static class InputFixture
    {
        public const string FixedEdgeV1Name = "edge-sequence-v1";
        public const int FixedEdgeV1TickCount = 60;

        public static IReadOnlyList<InputSample> CreateFixedEdgeV1()
        {
            var result = new List<InputSample>(FixedEdgeV1TickCount);
            var previous = TasAction.None;
            for (var tick = 0; tick < FixedEdgeV1TickCount; tick++)
            {
                var held = GetHeldForTick(tick);
                result.Add(InputSample.FromHeld((ulong)tick, held, previous));
                previous = held;
            }

            return new ReadOnlyCollection<InputSample>(result);
        }

        private static TasAction GetHeldForTick(int tick)
        {
            if (tick < 10 || tick >= 44)
            {
                return TasAction.None;
            }

            if (tick <= 29 || tick == 39 || tick == 41 || tick == 43)
            {
                return TasAction.Right;
            }

            if (tick <= 38)
            {
                return TasAction.Right | TasAction.Jump;
            }

            if (tick == 40)
            {
                return TasAction.Right | TasAction.Attack;
            }

            return TasAction.Right | TasAction.Dash;
        }
    }
}
