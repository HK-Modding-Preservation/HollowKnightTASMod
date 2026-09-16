using System;
using System.Collections.Generic;

namespace HollowKnightTAS.Core.Movie
{
    public sealed class MovieDocument
    {
        private readonly MovieCommand[] commands;

        public MovieDocument(
            string sourceName,
            MovieHeader header,
            IEnumerable<MovieCommand> commands)
        {
            SourceName = string.IsNullOrWhiteSpace(sourceName) ? "<movie>" : sourceName;
            Header = header ?? throw new ArgumentNullException(nameof(header));
            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            this.commands = new List<MovieCommand>(commands).ToArray();
        }

        public string SourceName { get; }
        public MovieHeader Header { get; }
        public IReadOnlyList<MovieCommand> Commands => commands;
    }

    public sealed class MovieParseResult
    {
        private readonly MovieDiagnostic[] diagnostics;

        internal MovieParseResult(
            MovieDocument? document,
            IEnumerable<MovieDiagnostic> diagnostics)
        {
            Document = document;
            this.diagnostics = new List<MovieDiagnostic>(diagnostics).ToArray();
        }

        public MovieDocument? Document { get; }
        public IReadOnlyList<MovieDiagnostic> Diagnostics => diagnostics;
        public bool Success => Document != null && diagnostics.Length == 0;
    }
}
