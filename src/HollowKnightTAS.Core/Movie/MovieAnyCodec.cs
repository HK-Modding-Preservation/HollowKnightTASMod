using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HollowKnightTAS.Core.Movie
{
    public sealed class MovieAnyParseResult
    {
        private readonly IReadOnlyList<MovieDiagnostic> diagnostics;
        internal MovieAnyParseResult(int version, MovieDocument? v1Document,
            MovieV2Document? v2Document, IEnumerable<MovieDiagnostic> diagnostics)
        {
            if (v1Document != null && v2Document != null)
                throw new ArgumentException("A movie cannot contain both protocol versions.");
            Version = version;
            V1Document = v1Document;
            V2Document = v2Document;
            this.diagnostics = Array.AsReadOnly(new List<MovieDiagnostic>(diagnostics).ToArray());
        }
        public int Version { get; }
        public MovieDocument? V1Document { get; }
        public MovieV2Document? V2Document { get; }
        public IReadOnlyList<MovieDiagnostic> Diagnostics => diagnostics;
        public bool Success => diagnostics.Count == 0
                               && (Version == 1 && V1Document != null && V2Document == null
                                   || Version == 2 && V2Document != null && V1Document == null);
    }

    public sealed class MovieAnyCodec
    {
        public MovieAnyParseResult Parse(TextReader reader, string sourceName)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            sourceName = string.IsNullOrWhiteSpace(sourceName) ? "<movie>" : sourceName;
            var source = ReadBounded(reader);
            if (source == null)
                return Failure(sourceName, MovieDiagnosticCodes.SourceTooLarge,
                    "Movie source exceeds the format detection limit.");

            var version = DetectVersion(source);
            using (var input = new StringReader(source))
            {
                if (version == MovieProtocolV1.Version)
                {
                    var parsed = new MovieParser().Parse(input, sourceName);
                    return new MovieAnyParseResult(version, parsed.Document, null, parsed.Diagnostics);
                }
                if (version == MovieProtocolV2.Version)
                {
                    var parsed = new MovieV2Codec().Parse(input, sourceName);
                    return new MovieAnyParseResult(version, null, parsed.Document, parsed.Diagnostics);
                }
            }
            return Failure(sourceName, MovieDiagnosticCodes.UnsupportedVersion,
                "No supported hktas format identifier was found.");
        }

        private static MovieAnyParseResult Failure(string sourceName, string code, string message)
            => new MovieAnyParseResult(0, null, null, new[]
            {
                new MovieDiagnostic(code, new MovieSourceSpan(sourceName, 1, 1, 1),
                    message, "Open a v1 or v2 .hktas movie.")
            });

        private static string? ReadBounded(TextReader reader)
        {
            var builder = new StringBuilder();
            var buffer = new char[4096];
            while (true)
            {
                var read = reader.Read(buffer, 0, buffer.Length);
                if (read == 0) return builder.ToString();
                if (read < 0 || builder.Length > MovieParser.MaximumSourceCharacters - read)
                    return null;
                builder.Append(buffer, 0, read);
            }
        }

        private static int DetectVersion(string source)
        {
            using (var lines = new StringReader(source))
            {
                string? line;
                while ((line = lines.ReadLine()) != null)
                {
                    var trimmed = line.TrimStart(' ', '\t', '\r');
                    if (trimmed.Length == 0 || trimmed[0] == '#') continue;
                    if (trimmed[0] == '\uFEFF') trimmed = trimmed.Substring(1).TrimStart(' ', '\t');
                    if (trimmed.StartsWith("{", StringComparison.Ordinal)) return MovieProtocolV2.Version;
                    if (trimmed.StartsWith("hktas", StringComparison.Ordinal)
                        && (trimmed.Length == 5 || char.IsWhiteSpace(trimmed[5])))
                        return MovieProtocolV1.Version;
                    return 0;
                }
            }
            return 0;
        }
    }
}
