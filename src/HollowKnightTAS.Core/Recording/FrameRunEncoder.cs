using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Core.Recording
{
    public static class FrameRunEncoder
    {
        public static IReadOnlyList<FrameRunCommand> Encode(
            IEnumerable<InputSample> samples,
            string sourceName = "<recording>")
        {
            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }

            var commands = new List<FrameRunCommand>();
            var hasRun = false;
            TasAction held = TasAction.None;
            short axisX = 0;
            short axisY = 0;
            long count = 0;
            foreach (var sample in samples)
            {
                if (hasRun
                    && sample.Held == held
                    && sample.AxisX == axisX
                    && sample.AxisY == axisY)
                {
                    count = checked(count + 1);
                    continue;
                }

                if (hasRun)
                {
                    commands.Add(Create(held, axisX, axisY, count, sourceName));
                }

                held = sample.Held;
                axisX = sample.AxisX;
                axisY = sample.AxisY;
                count = 1;
                hasRun = true;
            }

            if (hasRun)
            {
                commands.Add(Create(held, axisX, axisY, count, sourceName));
            }

            return new ReadOnlyCollection<FrameRunCommand>(commands);
        }

        public static IReadOnlyList<InputSample> Expand(
            IEnumerable<FrameRunCommand> commands)
        {
            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            var result = new List<InputSample>();
            var previous = TasAction.None;
            ulong tick = 0;
            foreach (var command in commands)
            {
                if (command == null || command.FrameCount <= 0)
                {
                    throw new ArgumentException(
                        "Frame runs must be non-null and positive.",
                        nameof(commands));
                }

                for (long offset = 0; offset < command.FrameCount; offset++)
                {
                    var sample = InputSample.FromHeld(
                        tick,
                        command.HeldActions,
                        previous,
                        checked((short)command.AxisX),
                        checked((short)command.AxisY));
                    result.Add(sample);
                    previous = command.HeldActions;
                    tick = checked(tick + 1);
                }
            }

            return new ReadOnlyCollection<InputSample>(result);
        }

        private static FrameRunCommand Create(
            TasAction held,
            short axisX,
            short axisY,
            long count,
            string sourceName)
        {
            return new FrameRunCommand(
                count,
                held,
                axisX,
                axisY,
                axisX != 0 || axisY != 0,
                new MovieSourceSpan(sourceName, 1, 1, 1));
        }
    }
}
