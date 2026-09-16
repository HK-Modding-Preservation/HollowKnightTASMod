using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Companion.Automation
{
    public sealed class InputBatchTransactionWorkspace
    {
        private const int MaximumChunkBytes = 4 * 1024 * 1024;
        private readonly object sync = new object();
        private readonly Dictionary<string, Transaction> transactions =
            new Dictionary<string, Transaction>(StringComparer.Ordinal);
        private readonly MovieEditorService editor = new MovieEditorService();

        public InputBatchTransactionResult Begin(
            string clientId,
            string connectionId,
            string leaseId,
            string sessionId,
            string manifestSha256,
            long expectedMovieTick,
            int expectedSceneEpoch)
        {
            if (expectedMovieTick < 0 || expectedSceneEpoch < 0)
            {
                return InputBatchTransactionResult.Fail(
                    "InvalidBinding",
                    "Input-batch tick and scene epoch must be non-negative.");
            }

            var id = "inputbatch-" + Guid.NewGuid().ToString("N");
            var transaction = new Transaction(
                id,
                Require(clientId, nameof(clientId)),
                Require(connectionId, nameof(connectionId)),
                Require(leaseId, nameof(leaseId)),
                Require(sessionId, nameof(sessionId)),
                Require(manifestSha256, nameof(manifestSha256)),
                expectedMovieTick,
                expectedSceneEpoch);
            lock (sync)
            {
                transactions.Add(id, transaction);
            }

            return transaction.Result(
                true,
                "Begun",
                "Input-batch transaction opened; no Runtime state was changed.",
                null,
                string.Empty);
        }

        public InputBatchTransactionResult Append(
            string transactionId,
            string clientId,
            string connectionId,
            string leaseId,
            long expectedMovieTick,
            int expectedSceneEpoch,
            int chunkIndex,
            byte[] candidateMovieUtf8)
        {
            lock (sync)
            {
                if (!TryBound(
                        transactionId,
                        clientId,
                        connectionId,
                        leaseId,
                        expectedMovieTick,
                        expectedSceneEpoch,
                        out var transaction,
                        out var error))
                {
                    return error!;
                }

                if (chunkIndex != transaction!.Chunks.Count)
                {
                    return transaction.Result(
                        false,
                        "ChunkOrder",
                        "Input-batch chunks must be appended exactly once in zero-based order.",
                        null,
                        string.Empty);
                }

                if (candidateMovieUtf8 == null
                    || candidateMovieUtf8.Length == 0
                    || candidateMovieUtf8.Length > MaximumChunkBytes)
                {
                    return transaction.Result(
                        false,
                        "ChunkSize",
                        "An input-batch chunk must contain 1 to 4194304 UTF-8 bytes.",
                        null,
                        string.Empty);
                }

                string source;
                try
                {
                    source = new UTF8Encoding(false, true)
                        .GetString(candidateMovieUtf8);
                }
                catch (DecoderFallbackException exception)
                {
                    return transaction.Result(
                        false,
                        "MalformedUtf8",
                        exception.Message,
                        null,
                        string.Empty);
                }

                var validated = editor.Validate(
                    source,
                    "input-batch-chunk-"
                    + chunkIndex.ToString(CultureInfo.InvariantCulture)
                    + ".hktas");
                if (!validated.Success || validated.Document == null)
                {
                    var detail = validated.Diagnostics.Count == 0
                        ? "Input-batch chunk validation failed."
                        : validated.Diagnostics[0].Code
                          + ":"
                          + validated.Diagnostics[0].Message;
                    return transaction.Result(
                        false,
                        "ChunkInvalid",
                        detail,
                        null,
                        string.Empty);
                }

                if (validated.ExpandedTicks < 1
                    || validated.Document.Commands.Any(
                        command => !(command is FrameRunCommand)))
                {
                    return transaction.Result(
                        false,
                        "InputOnlyChunkRequired",
                        "Every transaction chunk must contain one or more frames commands only.",
                        null,
                        string.Empty);
                }

                if (!transaction.AcceptsHeader(validated.Document.Header))
                {
                    return transaction.Result(
                        false,
                        "ChunkHeaderMismatch",
                        "Every chunk must use the exact header of the first chunk and the bound Runtime manifest.",
                        null,
                        string.Empty);
                }

                long nextTicks;
                try
                {
                    nextTicks = checked(
                        transaction.ExpandedTicks + validated.ExpandedTicks);
                }
                catch (OverflowException)
                {
                    return transaction.Result(
                        false,
                        "BatchSize",
                        "Input-batch expanded tick count overflowed Int64.",
                        null,
                        string.Empty);
                }

                if (nextTicks > MovieProtocolV1.DefaultMaxExpandedTicks)
                {
                    return transaction.Result(
                        false,
                        "BatchSize",
                        "Input-batch expanded ticks exceed the HK-TAS Movie v1 limit of "
                        + MovieProtocolV1.DefaultMaxExpandedTicks.ToString(
                            CultureInfo.InvariantCulture)
                        + ".",
                        null,
                        string.Empty);
                }

                var canonicalBytes = new UTF8Encoding(false).GetBytes(
                    validated.CanonicalText);
                transaction.Append(
                    validated.Document,
                    validated.ExpandedTicks,
                    Sha256Utility.ComputeHex(canonicalBytes));
                return transaction.Result(
                    true,
                    "Appended",
                    "Input-batch chunk validated and staged; no Runtime state was changed.",
                    null,
                    string.Empty);
            }
        }

        public InputBatchTransactionResult Commit(
            string transactionId,
            string clientId,
            string connectionId,
            string leaseId,
            long expectedMovieTick,
            int expectedSceneEpoch)
        {
            lock (sync)
            {
                if (!TryBound(
                        transactionId,
                        clientId,
                        connectionId,
                        leaseId,
                        expectedMovieTick,
                        expectedSceneEpoch,
                        out var transaction,
                        out var error))
                {
                    return error!;
                }

                if (transaction!.Chunks.Count == 0
                    || transaction.Header == null)
                {
                    return transaction.Result(
                        false,
                        "EmptyBatch",
                        "Append at least one validated chunk before commit.",
                        null,
                        string.Empty);
                }

                var combined = new MovieDocument(
                    "input-batch-transaction.hktas",
                    transaction.Header,
                    transaction.Chunks.SelectMany(
                            chunk => chunk.Document.Commands)
                        .ToArray());
                var canonical = new MovieCanonicalWriter().WriteUtf8(combined);
                if (canonical.Length > 32 * 1024 * 1024)
                {
                    return transaction.Result(
                        false,
                        "BatchBytes",
                        "Committed input-batch movie exceeds the Runtime upload limit.",
                        null,
                        string.Empty);
                }

                var finalValidation = editor.Validate(
                    new UTF8Encoding(false, true).GetString(canonical),
                    "input-batch-transaction.hktas");
                if (!finalValidation.Success
                    || finalValidation.Document == null
                    || finalValidation.ExpandedTicks
                       != transaction.ExpandedTicks)
                {
                    return transaction.Result(
                        false,
                        "TransactionInvalid",
                        "Combined input-batch transaction failed canonical validation.",
                        null,
                        string.Empty);
                }

                var movieId = new MovieCanonicalWriter().ComputeMovieId(
                    finalValidation.Document);
                return transaction.Result(
                    true,
                    "Committed",
                    "Input-batch transaction is canonical and ready for atomic Runtime scheduling.",
                    canonical,
                    movieId);
            }
        }

        public InputBatchTransactionResult Complete(string transactionId)
        {
            lock (sync)
            {
                if (!transactions.Remove(transactionId, out var transaction))
                {
                    return InputBatchTransactionResult.Fail(
                        "TransactionNotFound",
                        "Input-batch transaction does not exist.");
                }

                return transaction.Result(
                    true,
                    "Completed",
                    "Input-batch transaction was consumed and removed.",
                    null,
                    string.Empty);
            }
        }

        public InputBatchTransactionResult Cancel(
            string transactionId,
            string clientId,
            string connectionId,
            string leaseId,
            long expectedMovieTick,
            int expectedSceneEpoch)
        {
            lock (sync)
            {
                if (!TryBound(
                        transactionId,
                        clientId,
                        connectionId,
                        leaseId,
                        expectedMovieTick,
                        expectedSceneEpoch,
                        out var transaction,
                        out var error))
                {
                    return error!;
                }

                transactions.Remove(transactionId);
                return transaction!.Result(
                    true,
                    "Cancelled",
                    "Input-batch transaction was cancelled without changing Runtime state.",
                    null,
                    string.Empty);
            }
        }

        public void ReleaseConnection(string connectionId)
        {
            lock (sync)
            {
                foreach (var id in transactions.Values
                             .Where(
                                 value => string.Equals(
                                     value.ConnectionId,
                                     connectionId,
                                     StringComparison.Ordinal))
                             .Select(value => value.Id)
                             .ToArray())
                {
                    transactions.Remove(id);
                }
            }
        }

        public void Clear()
        {
            lock (sync)
            {
                transactions.Clear();
            }
        }

        private bool TryBound(
            string transactionId,
            string clientId,
            string connectionId,
            string leaseId,
            long expectedMovieTick,
            int expectedSceneEpoch,
            out Transaction? transaction,
            out InputBatchTransactionResult? error)
        {
            transaction = null;
            error = null;
            if (!transactions.TryGetValue(transactionId, out var found))
            {
                error = InputBatchTransactionResult.Fail(
                    "TransactionNotFound",
                    "Input-batch transaction does not exist.");
                return false;
            }

            if (!found.Matches(
                    clientId,
                    connectionId,
                    leaseId,
                    expectedMovieTick,
                    expectedSceneEpoch))
            {
                error = found.Result(
                    false,
                    "TransactionBindingMismatch",
                    "Transaction owner, lease, tick, or scene epoch changed.",
                    null,
                    string.Empty);
                return false;
            }

            transaction = found;
            return true;
        }

        private static string Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty value is required.",
                    name);
            }

            return value;
        }

        private sealed class Transaction
        {
            public Transaction(
                string id,
                string clientId,
                string connectionId,
                string leaseId,
                string sessionId,
                string manifestSha256,
                long expectedMovieTick,
                int expectedSceneEpoch)
            {
                Id = id;
                ClientId = clientId;
                ConnectionId = connectionId;
                LeaseId = leaseId;
                SessionId = sessionId;
                ManifestSha256 = manifestSha256;
                ExpectedMovieTick = expectedMovieTick;
                ExpectedSceneEpoch = expectedSceneEpoch;
            }

            public string Id { get; }
            public string ClientId { get; }
            public string ConnectionId { get; }
            public string LeaseId { get; }
            public string SessionId { get; }
            public string ManifestSha256 { get; }
            public long ExpectedMovieTick { get; }
            public int ExpectedSceneEpoch { get; }
            public MovieHeader? Header { get; private set; }
            public List<Chunk> Chunks { get; } = new List<Chunk>();
            public long ExpandedTicks { get; private set; }

            public bool Matches(
                string clientId,
                string connectionId,
                string leaseId,
                long expectedMovieTick,
                int expectedSceneEpoch)
            {
                return string.Equals(ClientId, clientId, StringComparison.Ordinal)
                       && string.Equals(
                           ConnectionId,
                           connectionId,
                           StringComparison.Ordinal)
                       && string.Equals(LeaseId, leaseId, StringComparison.Ordinal)
                       && ExpectedMovieTick == expectedMovieTick
                       && ExpectedSceneEpoch == expectedSceneEpoch;
            }

            public bool AcceptsHeader(MovieHeader value)
            {
                if (!string.Equals(
                        value.ManifestSha256,
                        ManifestSha256,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                if (Header == null)
                {
                    return true;
                }

                return Header.ProtocolVersion == value.ProtocolVersion
                       && Header.GameVersion == value.GameVersion
                       && Header.ApiVersion == value.ApiVersion
                       && Header.ManifestSha256 == value.ManifestSha256
                       && Header.BaselineId == value.BaselineId
                       && Header.BaselineSha256 == value.BaselineSha256
                       && Header.TickUnit == value.TickUnit;
            }

            public void Append(
                MovieDocument document,
                long expandedTicks,
                string chunkSha256)
            {
                Header ??= document.Header;
                Chunks.Add(new Chunk(document, chunkSha256));
                ExpandedTicks = checked(ExpandedTicks + expandedTicks);
            }

            public InputBatchTransactionResult Result(
                bool success,
                string code,
                string detail,
                byte[]? canonicalMovieUtf8,
                string movieId)
            {
                return new InputBatchTransactionResult(
                    success,
                    code,
                    detail,
                    Id,
                    Chunks.Count,
                    ExpandedTicks,
                    Chunks.Select(value => value.Sha256).ToArray(),
                    canonicalMovieUtf8,
                    movieId,
                    ExpectedMovieTick,
                    ExpectedSceneEpoch);
            }
        }

        private sealed class Chunk
        {
            public Chunk(MovieDocument document, string sha256)
            {
                Document = document;
                Sha256 = sha256;
            }

            public MovieDocument Document { get; }
            public string Sha256 { get; }
        }
    }

    public sealed class InputBatchTransactionResult
    {
        public InputBatchTransactionResult(
            bool success,
            string code,
            string detail,
            string transactionId,
            int chunkCount,
            long expandedTicks,
            IReadOnlyList<string> chunkSha256s,
            byte[]? canonicalMovieUtf8,
            string movieId,
            long expectedMovieTick,
            int expectedSceneEpoch)
        {
            Success = success;
            Code = code;
            Detail = detail;
            TransactionId = transactionId;
            ChunkCount = chunkCount;
            ExpandedTicks = expandedTicks;
            ChunkSha256s = chunkSha256s;
            CanonicalMovieUtf8 = canonicalMovieUtf8;
            MovieId = movieId;
            ExpectedMovieTick = expectedMovieTick;
            ExpectedSceneEpoch = expectedSceneEpoch;
        }

        public bool Success { get; }
        public string Code { get; }
        public string Detail { get; }
        public string TransactionId { get; }
        public int ChunkCount { get; }
        public long ExpandedTicks { get; }
        public IReadOnlyList<string> ChunkSha256s { get; }
        public byte[]? CanonicalMovieUtf8 { get; }
        public string MovieId { get; }
        public long ExpectedMovieTick { get; }
        public int ExpectedSceneEpoch { get; }

        public static InputBatchTransactionResult Fail(
            string code,
            string detail)
        {
            return new InputBatchTransactionResult(
                false,
                code,
                detail,
                string.Empty,
                0,
                0,
                Array.Empty<string>(),
                null,
                string.Empty,
                -1,
                -1);
        }
    }
}
