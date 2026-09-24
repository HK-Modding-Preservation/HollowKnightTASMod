using System;
using System.Collections.Generic;
using System.IO;

namespace HollowKnightTAS.Core.Movie
{
    public sealed class MovieV2ValidationContext
    {
        public MovieV2ValidationContext(long maxExpandedFrames, int maxSamplesPerFrame)
        {
            if (maxExpandedFrames < 1 || maxExpandedFrames > MovieProtocolV2.MaximumExpandedFrames)
                throw new ArgumentOutOfRangeException(nameof(maxExpandedFrames));
            if (maxSamplesPerFrame < 1 || maxSamplesPerFrame > MovieProtocolV2.MaximumSamplesPerFrame)
                throw new ArgumentOutOfRangeException(nameof(maxSamplesPerFrame));
            MaxExpandedFrames = maxExpandedFrames;
            MaxSamplesPerFrame = maxSamplesPerFrame;
        }

        public long MaxExpandedFrames { get; }
        public int MaxSamplesPerFrame { get; }

        public static MovieV2ValidationContext CreateDefault()
            => new MovieV2ValidationContext(MovieProtocolV2.MaximumExpandedFrames,
                MovieProtocolV2.MaximumSamplesPerFrame);
    }

    public sealed class MovieV2ValidationReport
    {
        private readonly IReadOnlyList<MovieDiagnostic> diagnostics;
        internal MovieV2ValidationReport(long expandedFrames, IEnumerable<MovieDiagnostic> diagnostics)
        {
            ExpandedFrames = expandedFrames;
            this.diagnostics = Array.AsReadOnly(new List<MovieDiagnostic>(diagnostics).ToArray());
        }
        public long ExpandedFrames { get; }
        public IReadOnlyList<MovieDiagnostic> Diagnostics => diagnostics;
        public bool Success => diagnostics.Count == 0;
    }

    public sealed class MovieV2Validator
    {
        public MovieV2ValidationReport Validate(MovieV2Document movie, MovieV2ValidationContext context)
        {
            if (movie == null) throw new ArgumentNullException(nameof(movie));
            if (context == null) throw new ArgumentNullException(nameof(context));
            var diagnostics = new List<MovieDiagnostic>();
            var headerSpan = new MovieSourceSpan(movie.SourceName, 1, 1, 1);
            if (!string.Equals(movie.Header.ActionSchemaId, MovieProtocolV2.ActionSchemaId, StringComparison.Ordinal))
                Add(diagnostics, MovieDiagnosticCodes.UnknownAction, headerSpan, "Unsupported v2 action schema.");

            long frames = 0;
            foreach (var run in movie.Runs)
            {
                var span = run.Span;
                if (run.RepeatCount < 1)
                    Add(diagnostics, MovieDiagnosticCodes.InvalidCommand, span, "Native-frame run count must be positive.");
                else if (run.RepeatCount > context.MaxExpandedFrames - frames)
                {
                    Add(diagnostics, MovieDiagnosticCodes.ExpandedTickLimit, span, "Expanded native-frame limit exceeded.");
                    frames = context.MaxExpandedFrames;
                }
                else frames += run.RepeatCount;

                if (run.Samples.Count > context.MaxSamplesPerFrame)
                    Add(diagnostics, MovieDiagnosticCodes.InvalidCommand, span, "Too many samples in a native frame.");
                foreach (var sample in run.Samples)
                {
                    int expected;
                    try { expected = MovieProtocolV2.ExpectedValueCount(sample.Channel); }
                    catch (ArgumentOutOfRangeException)
                    {
                        Add(diagnostics, MovieDiagnosticCodes.UnknownAction, span, "Unknown input channel.");
                        continue;
                    }
                    if (sample.Values.Count != expected)
                        Add(diagnostics, MovieDiagnosticCodes.InvalidCommand, span, "Input value count does not match its channel.");
                    var validMask = expected == 0 ? 0UL : (1UL << expected) - 1UL;
                    if ((sample.PressedMask | sample.ReleasedMask) > long.MaxValue
                        || ((sample.PressedMask | sample.ReleasedMask) & ~validMask) != 0)
                        Add(diagnostics, MovieDiagnosticCodes.InvalidCommand, span, "Action edge mask is invalid.");
                    if (MovieProtocolV2.IsMouseChannel(sample.Channel))
                    {
                        if (!movie.Header.MouseEnabled)
                            Add(diagnostics, MovieDiagnosticCodes.InvalidCommand, span, "Mouse sample is present while game mouse input is disabled.");
                        if (sample.Mouse == null || sample.Mouse.XQ16 < 0 || sample.Mouse.XQ16 > ushort.MaxValue
                            || sample.Mouse.YQ16 < 0 || sample.Mouse.YQ16 > ushort.MaxValue)
                            Add(diagnostics, MovieDiagnosticCodes.AxisRange, span, "Mouse coordinates are outside Q16 viewport range.");
                    }
                    else if (sample.Mouse != null)
                        Add(diagnostics, MovieDiagnosticCodes.InvalidCommand, span, "Action sample has unexpected mouse state.");
                }
            }

            if (diagnostics.Count == 0)
            {
                try
                {
                    // Canonical writing also checks encoded size and gives this document a stable identity.
                    new MovieV2Codec().ComputeMovieId(movie);
                }
                catch (InvalidDataException error)
                {
                    Add(diagnostics, MovieDiagnosticCodes.InvalidCommand, headerSpan, error.Message);
                }
            }
            return new MovieV2ValidationReport(frames, diagnostics);
        }

        private static void Add(ICollection<MovieDiagnostic> diagnostics, string code, MovieSourceSpan span, string message)
            => diagnostics.Add(new MovieDiagnostic(code, span, message, "Correct the v2 movie and retry."));
    }
}
