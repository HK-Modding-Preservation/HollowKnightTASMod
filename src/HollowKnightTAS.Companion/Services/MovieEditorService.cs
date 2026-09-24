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

    public sealed class MovieAnyEditorResult
    {
        private readonly IReadOnlyList<MovieDiagnostic> diagnostics;

        public MovieAnyEditorResult(int version, MovieDocument? v1Document,
            MovieV2Document? v2Document, string canonicalText, string movieId,
            long expandedFrames, IEnumerable<MovieDiagnostic> diagnostics)
        {
            Version = version;
            V1Document = v1Document;
            V2Document = v2Document;
            CanonicalText = canonicalText;
            MovieId = movieId;
            ExpandedFrames = expandedFrames;
            this.diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        }

        public int Version { get; }
        public MovieDocument? V1Document { get; }
        public MovieV2Document? V2Document { get; }
        public string CanonicalText { get; }
        public string MovieId { get; }
        public long ExpandedFrames { get; }
        public IReadOnlyList<MovieDiagnostic> Diagnostics => diagnostics;
        public bool Success => diagnostics.Count == 0 && (V1Document != null || V2Document != null);
    }

    public sealed class MovieEditorService
    {
        public MovieAnyEditorResult ValidateAny(string source, string sourceName = "<studio>")
        {
            var parse = new MovieAnyCodec().Parse(new StringReader(source ?? string.Empty), sourceName);
            if (!parse.Success)
                return new MovieAnyEditorResult(parse.Version, parse.V1Document, parse.V2Document,
                    string.Empty, string.Empty, 0, parse.Diagnostics);
            if (parse.Version == MovieProtocolV1.Version && parse.V1Document != null)
            {
                var report = new MovieValidator().Validate(parse.V1Document,
                    MovieValidationContext.CreateDefault());
                if (!report.Success)
                    return new MovieAnyEditorResult(1, parse.V1Document, null, string.Empty, string.Empty,
                        report.ExpandedTickCount, report.Diagnostics);
                var writer = new MovieCanonicalWriter();
                return new MovieAnyEditorResult(1, parse.V1Document, null,
                    writer.WriteToString(parse.V1Document), writer.ComputeMovieId(parse.V1Document),
                    report.ExpandedTickCount, Array.Empty<MovieDiagnostic>());
            }
            if (parse.Version == MovieProtocolV2.Version && parse.V2Document != null)
            {
                var report = new MovieV2Validator().Validate(parse.V2Document,
                    MovieV2ValidationContext.CreateDefault());
                if (!report.Success)
                    return new MovieAnyEditorResult(2, null, parse.V2Document, string.Empty, string.Empty,
                        report.ExpandedFrames, report.Diagnostics);
                var codec = new MovieV2Codec();
                return new MovieAnyEditorResult(2, null, parse.V2Document,
                    codec.WriteCanonical(parse.V2Document), codec.ComputeMovieId(parse.V2Document),
                    report.ExpandedFrames, Array.Empty<MovieDiagnostic>());
            }
            throw new InvalidOperationException("Movie format dispatch returned an unsupported version.");
        }

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
