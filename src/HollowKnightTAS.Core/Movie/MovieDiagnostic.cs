using System;

namespace HollowKnightTAS.Core.Movie
{
    public sealed class MovieDiagnostic
    {
        public MovieDiagnostic(
            string code,
            MovieSourceSpan span,
            string message,
            string action)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("A diagnostic code is required.", nameof(code));
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("A diagnostic message is required.", nameof(message));
            }

            Code = code;
            Span = span;
            Message = message;
            Action = string.IsNullOrWhiteSpace(action)
                ? "Correct the movie source and retry."
                : action;
        }

        public string Code { get; }
        public MovieSourceSpan Span { get; }
        public string Message { get; }
        public string Action { get; }

        public override string ToString()
        {
            return Span.Source
                   + "("
                   + Span.Line
                   + ","
                   + Span.Column
                   + "): "
                   + Code
                   + ": "
                   + Message
                   + " "
                   + Action;
        }
    }

    public static class MovieDiagnosticCodes
    {
        public const string SourceTooLarge = "HKTAS100";
        public const string LineTooLong = "HKTAS101";
        public const string InvalidCharacter = "HKTAS102";
        public const string InvalidEscape = "HKTAS103";
        public const string UnterminatedString = "HKTAS104";
        public const string InvalidSyntax = "HKTAS105";
        public const string UnknownHeader = "HKTAS110";
        public const string DuplicateHeader = "HKTAS111";
        public const string MissingHeader = "HKTAS112";
        public const string MissingSeparator = "HKTAS113";
        public const string InvalidHeaderValue = "HKTAS114";
        public const string UnknownCommand = "HKTAS120";
        public const string InvalidCommand = "HKTAS121";
        public const string DuplicateField = "HKTAS122";
        public const string UnknownAction = "HKTAS123";
        public const string InvalidAssertValue = "HKTAS124";
        public const string UnsupportedVersion = "HKTAS200";
        public const string InvalidIdentifier = "HKTAS201";
        public const string InvalidHash = "HKTAS202";
        public const string InvalidBaseline = "HKTAS203";
        public const string InvalidTickUnit = "HKTAS204";
        public const string ExpandedTickLimit = "HKTAS210";
        public const string DirectionConflict = "HKTAS211";
        public const string AxisRange = "HKTAS212";
        public const string AxisDirectionMix = "HKTAS213";
        public const string MarkerTooLarge = "HKTAS214";
        public const string UnknownSemanticPath = "HKTAS215";
        public const string ManifestMismatch = "HKTAS220";
        public const string BaselineMismatch = "HKTAS221";
    }
}
