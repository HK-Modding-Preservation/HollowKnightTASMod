using System;
using System.IO;
using System.Text;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Cli.Commands
{
    internal static class MovieCommandUtilities
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static LoadedAnyMovie LoadAndValidateAny(string path,
            MovieValidationContext v1Context, MovieV2ValidationContext v2Context,
            TextWriter standardError)
        {
            var fullPath = Path.GetFullPath(path);
            var length = new FileInfo(fullPath).Length;
            var maximumBytes = (long)Math.Max(MovieParser.MaximumSourceCharacters * 4,
                MovieProtocolV2.MaximumSourceUtf8Bytes);
            if (length > maximumBytes)
                throw new InvalidDataException("Movie file exceeds the safety limit: " + fullPath);
            var bytes = File.ReadAllBytes(fullPath);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                throw new InvalidDataException("Movie must be UTF-8 without BOM: " + fullPath);
            var source = StrictUtf8.GetString(bytes);
            var parsed = new MovieAnyCodec().Parse(new StringReader(source), fullPath);
            if (!parsed.Success)
            {
                WriteDiagnostics(parsed.Diagnostics, standardError);
                throw new MovieContentException();
            }
            if (parsed.V1Document != null)
            {
                var report = new MovieValidator().Validate(parsed.V1Document, v1Context);
                if (!report.Success)
                {
                    WriteDiagnostics(report.Diagnostics, standardError);
                    throw new MovieContentException();
                }
                return new LoadedAnyMovie(fullPath, bytes, parsed.V1Document,
                    null, report.ExpandedTickCount);
            }
            var v2 = parsed.V2Document!;
            if (bytes.Length > MovieProtocolV2.MaximumSourceUtf8Bytes)
                throw new InvalidDataException("v2 movie exceeds the UTF-8 source limit: " + fullPath);
            var v2Report = new MovieV2Validator().Validate(v2, v2Context);
            if (!v2Report.Success)
            {
                WriteDiagnostics(v2Report.Diagnostics, standardError);
                throw new MovieContentException();
            }
            return new LoadedAnyMovie(fullPath, bytes, null, v2,
                v2Report.ExpandedFrames);
        }

        public static LoadedMovie LoadAndValidate(
            string path,
            MovieValidationContext context,
            TextWriter standardError)
        {
            var fullPath = Path.GetFullPath(path);
            var fileLength = new FileInfo(fullPath).Length;
            var maximumBytes = (long)MovieParser.MaximumSourceCharacters * 4;
            if (fileLength > maximumBytes)
            {
                throw new InvalidDataException(
                    "Movie file exceeds the "
                    + maximumBytes
                    + "-byte safety limit: "
                    + fullPath);
            }

            var bytes = File.ReadAllBytes(fullPath);
            if (bytes.Length >= 3
                && bytes[0] == 0xEF
                && bytes[1] == 0xBB
                && bytes[2] == 0xBF)
            {
                throw new InvalidDataException(
                    "Movie must be UTF-8 without BOM: " + fullPath);
            }

            if (StrictUtf8.GetCharCount(bytes) > MovieParser.MaximumSourceCharacters)
            {
                throw new InvalidDataException(
                    "Decoded movie exceeds the "
                    + MovieParser.MaximumSourceCharacters
                    + "-character safety limit: "
                    + fullPath);
            }

            var text = StrictUtf8.GetString(bytes);
            var parser = new MovieParser();
            MovieParseResult parseResult;
            using (var reader = new StringReader(text))
            {
                parseResult = parser.Parse(reader, fullPath);
            }

            if (!parseResult.Success || parseResult.Document == null)
            {
                WriteDiagnostics(parseResult.Diagnostics, standardError);
                throw new MovieContentException();
            }

            var report = new MovieValidator().Validate(parseResult.Document, context);
            if (!report.Success)
            {
                WriteDiagnostics(report.Diagnostics, standardError);
                throw new MovieContentException();
            }

            return new LoadedMovie(fullPath, bytes, parseResult.Document, report);
        }

        public static void WriteDiagnostics(
            System.Collections.Generic.IEnumerable<MovieDiagnostic> diagnostics,
            TextWriter standardError)
        {
            foreach (var diagnostic in diagnostics)
            {
                standardError.WriteLine("INVALID " + Sanitize(diagnostic.ToString()));
            }
        }

        public static string Sanitize(string value)
        {
            return value.Replace('\r', ' ').Replace('\n', ' ');
        }
    }

    internal sealed class LoadedAnyMovie
    {
        public LoadedAnyMovie(string path, byte[] bytes, MovieDocument? v1Document,
            MovieV2Document? v2Document, long expandedFrames)
        {
            Path = path;
            Bytes = bytes;
            V1Document = v1Document;
            V2Document = v2Document;
            ExpandedFrames = expandedFrames;
        }
        public string Path { get; }
        public byte[] Bytes { get; }
        public MovieDocument? V1Document { get; }
        public MovieV2Document? V2Document { get; }
        public long ExpandedFrames { get; }
        public int Version => V2Document == null ? 1 : 2;
    }

    internal sealed class LoadedMovie
    {
        public LoadedMovie(
            string path,
            byte[] bytes,
            MovieDocument document,
            MovieValidationReport report)
        {
            Path = path;
            Bytes = bytes;
            Document = document;
            Report = report;
        }

        public string Path { get; }
        public byte[] Bytes { get; }
        public MovieDocument Document { get; }
        public MovieValidationReport Report { get; }
    }

    internal sealed class MovieContentException : Exception
    {
    }
}
