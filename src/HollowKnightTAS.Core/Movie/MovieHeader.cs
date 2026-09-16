using System;
using System.Collections.Generic;

namespace HollowKnightTAS.Core.Movie
{
    public sealed class MovieHeader
    {
        private readonly IReadOnlyDictionary<string, MovieSourceSpan> spans;

        public MovieHeader(
            int protocolVersion,
            string gameVersion,
            string apiVersion,
            string manifestSha256,
            string baselineId,
            string baselineSha256,
            string tickUnit)
            : this(
                protocolVersion,
                gameVersion,
                apiVersion,
                manifestSha256,
                baselineId,
                baselineSha256,
                tickUnit,
                new Dictionary<string, MovieSourceSpan>(StringComparer.Ordinal))
        {
        }

        internal MovieHeader(
            int protocolVersion,
            string gameVersion,
            string apiVersion,
            string manifestSha256,
            string baselineId,
            string baselineSha256,
            string tickUnit,
            IReadOnlyDictionary<string, MovieSourceSpan> spans)
        {
            ProtocolVersion = protocolVersion;
            GameVersion = gameVersion ?? throw new ArgumentNullException(nameof(gameVersion));
            ApiVersion = apiVersion ?? throw new ArgumentNullException(nameof(apiVersion));
            ManifestSha256 = manifestSha256
                             ?? throw new ArgumentNullException(nameof(manifestSha256));
            BaselineId = baselineId ?? throw new ArgumentNullException(nameof(baselineId));
            BaselineSha256 = baselineSha256
                             ?? throw new ArgumentNullException(nameof(baselineSha256));
            TickUnit = tickUnit ?? throw new ArgumentNullException(nameof(tickUnit));
            this.spans = spans ?? throw new ArgumentNullException(nameof(spans));
        }

        public int ProtocolVersion { get; }
        public string GameVersion { get; }
        public string ApiVersion { get; }
        public string ManifestSha256 { get; }
        public string BaselineId { get; }
        public string BaselineSha256 { get; }
        public string TickUnit { get; }

        public MovieSourceSpan GetSpan(string key, string fallbackSource = "<movie>")
        {
            if (key != null && spans.TryGetValue(key, out var span))
            {
                return span;
            }

            return new MovieSourceSpan(fallbackSource, 1, 1, 1);
        }
    }
}
