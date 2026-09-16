using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Companion.Services
{
    public sealed class MovieEditorResult
    {
        public MovieEditorResult(
            bool success,
            MovieDocument? document,
            string canonicalText,
            string movieId,
            long expandedTicks,
            IReadOnlyList<MovieDiagnostic> diagnostics)
        {
            Success = success;
            Document = document;
            CanonicalText = canonicalText;
            MovieId = movieId;
            ExpandedTicks = expandedTicks;
            Diagnostics = diagnostics;
        }

        public bool Success { get; }
        public MovieDocument? Document { get; }
        public string CanonicalText { get; }
        public string MovieId { get; }
        public long ExpandedTicks { get; }
        public IReadOnlyList<MovieDiagnostic> Diagnostics { get; }
    }

    public sealed class MovieEditorService
    {
        public MovieEditorResult Validate(
            string source,
            string sourceName = "<studio>")
        {
            var parse = new MovieParser().Parse(
                new StringReader(source ?? string.Empty),
                sourceName);
            if (!parse.Success || parse.Document == null)
            {
                return new MovieEditorResult(
                    false,
                    parse.Document,
                    string.Empty,
                    string.Empty,
                    0,
                    parse.Diagnostics);
            }

            var validation = new MovieValidator().Validate(
                parse.Document,
                MovieValidationContext.CreateDefault());
            var diagnostics = parse.Diagnostics
                .Concat(validation.Diagnostics)
                .ToArray();
            if (diagnostics.Length != 0)
            {
                return new MovieEditorResult(
                    false,
                    parse.Document,
                    string.Empty,
                    string.Empty,
                    validation.ExpandedTickCount,
                    diagnostics);
            }

            var writer = new MovieCanonicalWriter();
            return new MovieEditorResult(
                true,
                parse.Document,
                writer.WriteToString(parse.Document),
                writer.ComputeMovieId(parse.Document),
                validation.ExpandedTickCount,
                diagnostics);
        }
    }
}
