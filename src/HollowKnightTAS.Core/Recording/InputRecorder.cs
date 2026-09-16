using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Core.Recording
{
    public sealed class InputRecorder
    {
        private readonly List<InputSample> samples = new List<InputSample>();
        private ulong lastInputTick;
        private bool hasTick;

        public bool HasGap { get; private set; }
        public int Count => samples.Count;
        public IReadOnlyList<InputSample> Samples =>
            new ReadOnlyCollection<InputSample>(samples);

        public bool Append(InputSample sample)
        {
            if (HasGap)
            {
                return false;
            }

            if (hasTick && sample.InputTick != lastInputTick + 1)
            {
                HasGap = true;
                return false;
            }

            samples.Add(sample);
            lastInputTick = sample.InputTick;
            hasTick = true;
            return true;
        }

        public MovieDocument CreateMovie(
            MovieHeader header,
            string sourceName = "<recording>")
        {
            if (header == null)
            {
                throw new ArgumentNullException(nameof(header));
            }

            if (HasGap)
            {
                throw new InvalidOperationException(
                    "A recording with an input gap cannot become a movie.");
            }

            return new MovieDocument(
                sourceName,
                header,
                FrameRunEncoder.Encode(samples, sourceName));
        }
    }
}
