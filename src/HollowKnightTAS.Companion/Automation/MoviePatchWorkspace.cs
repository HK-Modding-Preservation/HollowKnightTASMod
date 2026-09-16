using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.ReplaySave;
using HollowKnightTAS.Core.Cryptography;

namespace HollowKnightTAS.Companion.Automation
{
    public sealed class MovieBranchCatalogItem
    {
        public string BranchId { get; set; } = string.Empty;
        public bool IncludesLifecycle { get; set; }
        public DateTime LastWriteUtc { get; set; }
        public long Bytes { get; set; }
        public string Verification { get; set; } = "NotChecked";
        public string Display => $"{LastWriteUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {(IncludesLifecycle ? "完整记录" : "输入脚本")} · {BranchId.Substring(0, Math.Min(12, BranchId.Length))} · 未校验";
    }

    public sealed class MovieBranchResult
    {
        public MovieBranchResult(
            bool success,
            string code,
            string detail,
            string branchMovieId,
            byte[]? canonicalMovieUtf8,
            long expandedTicks)
        {
            Success = success;
            Code = code;
            Detail = detail;
            BranchMovieId = branchMovieId;
            CanonicalMovieUtf8 = canonicalMovieUtf8;
            ExpandedTicks = expandedTicks;
        }

        public bool Success { get; }
        public string Code { get; }
        public string Detail { get; }
        public string BranchMovieId { get; }
        public byte[]? CanonicalMovieUtf8 { get; }
        public long ExpandedTicks { get; }
    }

    public sealed class MoviePatchWorkspace
    {
        private const int MaximumCandidateBytes = 32 * 1024 * 1024;
        private readonly object sync = new object();
        private readonly string branchesRoot;
        private readonly string? legacyArtifactsRoot;
        private readonly MovieEditorService editor =
            new MovieEditorService();

        public MoviePatchWorkspace(string root, string sessionId, string? persistentBranchesRoot = null)
        {
            branchesRoot = persistentBranchesRoot == null ? Path.Combine(
                Path.GetFullPath(root),
                sessionId,
                "movie-branches") : Path.GetFullPath(persistentBranchesRoot);
            legacyArtifactsRoot = persistentBranchesRoot == null ? null : Path.GetFullPath(root);
            Directory.CreateDirectory(branchesRoot);
        }

        public MovieBranchResult Validate(byte[] candidateUtf8)
        {
            if (candidateUtf8 == null
                || candidateUtf8.Length == 0
                || candidateUtf8.Length > MaximumCandidateBytes)
            {
                return Fail(
                    "InvalidMovie",
                    "Candidate movie size is outside protocol bounds.");
            }

            string source;
            try
            {
                source = new UTF8Encoding(false, true)
                    .GetString(candidateUtf8);
            }
            catch (DecoderFallbackException exception)
            {
                return Fail("MalformedUtf8", exception.Message);
            }

            var validation = editor.Validate(
                source,
                "automation-proposal.hktas");
            if (!validation.Success
                || validation.Document == null)
            {
                var detail = validation.Diagnostics.Count == 0
                    ? "Movie validation failed."
                    : validation.Diagnostics[0].Code
                      + ":"
                      + validation.Diagnostics[0].Message;
                return Fail("MovieInvalid", detail);
            }

            var bytes = new MovieCanonicalWriter().WriteUtf8(
                validation.Document);
            return new MovieBranchResult(
                true,
                "Valid",
                "Candidate movie is canonical and valid.",
                validation.MovieId,
                bytes,
                validation.ExpandedTicks);
        }

        public string GetLifecycleSourceId(byte[] sourceMovie, ReplayLifecycleLog lifecycle)
        {
            var validated = Validate(sourceMovie);
            if (!validated.Success || validated.CanonicalMovieUtf8 == null)
                throw new InvalidDataException("Invalid lifecycle source movie.");
            lifecycle.VerifyInputPrefixes(ParseCanonical(validated.CanonicalMovieUtf8, "lifecycle-source"));
            return Sha256Utility.ComputeHex(Encoding.UTF8.GetBytes(
                "hkl-source-v1\n" + validated.BranchMovieId + "\n"
                + Sha256Utility.ComputeHex(lifecycle.Serialize())));
        }

        public LifecycleMovieBranch EditLifecycleTimeline(string expectedSourceId, byte[] sourceMovie,
            ReplayLifecycleLog lifecycle, IReadOnlyDictionary<string, byte[]> objects,
            TimelineEditKind kind, long startTick, long deleteCount, byte[] replacementMovie, byte[]? rootBaseline = null)
        {
            var capturedSource = (byte[])sourceMovie.Clone();
            if (!string.Equals(expectedSourceId, GetLifecycleSourceId(capturedSource, lifecycle), StringComparison.Ordinal))
                throw new InvalidOperationException("Lifecycle source changed; refresh before editing.");
            return new LifecycleMovieBranchStore(Path.Combine(branchesRoot, "lifecycle")).Store(
                capturedSource, lifecycle, objects, kind, startTick, deleteCount, replacementMovie, rootBaseline);
        }

        public LifecycleMovieBranch ReadLifecycleBranch(string branchId)
        {
            var path = FindBranchPath(branchId, lifecycle: true)
                ?? throw new FileNotFoundException("Lifecycle branch does not exist.");
            return new LifecycleMovieBranchStore(Path.GetDirectoryName(path)!).Read(branchId);
        }

        public LifecycleMovieBranch EditLifecycleBranch(string branchId, TimelineEditKind kind,
            long startTick, long deleteCount, byte[] replacementMovie)
        {
            var path = FindBranchPath(branchId, lifecycle: true)
                ?? throw new FileNotFoundException("Lifecycle branch does not exist.");
            var store = new LifecycleMovieBranchStore(Path.Combine(branchesRoot, "lifecycle"));
            // Validate and publish the exact bytes before editing a legacy branch.
            // Never move or rewrite the original session archive.
            store.ImportFile(path, branchId);
            return store.Edit(branchId, kind, startTick, deleteCount, replacementMovie);
        }

        public bool HasLifecycleBranch(string branchId) => MovieProtocolV1.IsLowerSha256(branchId)
            && FindBranchPath(branchId, lifecycle: true) != null;

        public IReadOnlyList<MovieBranchCatalogItem> ListBranches(int offset, int count = 50)
        {
            if (offset < 0 || count < 1 || count > 51) throw new ArgumentOutOfRangeException(nameof(offset));
            var roots = new List<string> { branchesRoot };
            if (legacyArtifactsRoot != null && Directory.Exists(legacyArtifactsRoot))
                foreach (var session in Directory.EnumerateDirectories(legacyArtifactsRoot))
                    if ((File.GetAttributes(session) & FileAttributes.ReparsePoint) == 0)
                        roots.Add(Path.Combine(session, "movie-branches"));
            var entries = new Dictionary<string, MovieBranchCatalogItem>(StringComparer.Ordinal);
            foreach (var root in roots)
            {
                if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (var lifecycle in new[] { false, true })
                {
                    var directory = lifecycle ? Path.Combine(root, "lifecycle") : root;
                    if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                    foreach (var path in Directory.EnumerateFiles(directory, lifecycle ? "*.hklbranch" : "*.hktas"))
                    {
                        var file = new FileInfo(path);
                        var id = Path.GetFileNameWithoutExtension(path);
                        if (!MovieProtocolV1.IsLowerSha256(id) || (file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        var key = (lifecycle ? "L" : "M") + id;
                        if (!entries.ContainsKey(key)) entries.Add(key, new MovieBranchCatalogItem
                        { BranchId = id, IncludesLifecycle = lifecycle, LastWriteUtc = file.LastWriteTimeUtc, Bytes = file.Length });
                    }
                }
            }
            // Listing is metadata-only, never a claim that a movie or root is valid.
            return entries.Values.OrderByDescending(x => x.LastWriteUtc).ThenBy(x => x.BranchId, StringComparer.Ordinal)
                .ThenBy(x => x.IncludesLifecycle).Skip(offset).Take(count).ToArray();
        }

        private string? FindBranchPath(string branchId, bool lifecycle)
        {
            if (!MovieProtocolV1.IsLowerSha256(branchId)) throw new InvalidDataException("Invalid branch ID.");
            var name = branchId + (lifecycle ? ".hklbranch" : ".hktas");
            string InRoot(string directory) => Path.Combine(lifecycle ? Path.Combine(directory, "lifecycle") : directory, name);
            var current = InRoot(branchesRoot);
            if (File.Exists(current)) return current;
            if (legacyArtifactsRoot == null || !Directory.Exists(legacyArtifactsRoot)) return null;
            // Only probe the requested hash at the known session layout, never
            // recursively enumerate artifacts or deserialize unrelated movies.
            foreach (var session in Directory.EnumerateDirectories(legacyArtifactsRoot))
            {
                if ((File.GetAttributes(session) & FileAttributes.ReparsePoint) != 0) continue;
                var directory = Path.Combine(session, "movie-branches");
                if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                var selected = lifecycle ? Path.Combine(directory, "lifecycle") : directory;
                if (!Directory.Exists(selected) || (File.GetAttributes(selected) & FileAttributes.ReparsePoint) != 0) continue;
                var path = Path.Combine(selected, name);
                if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) return path;
            }
            return null;
        }

        public MovieBranchResult Propose(
            string suppliedBaseMovieId,
            string currentBaseMovieId,
            byte[] candidateUtf8)
        {
            var expectedBase = string.IsNullOrEmpty(currentBaseMovieId)
                ? "none"
                : currentBaseMovieId;
            if (!string.Equals(
                    suppliedBaseMovieId,
                    expectedBase,
                    StringComparison.Ordinal))
            {
                return Fail(
                    "BaseMovieChanged",
                    "Proposal base movie does not match current movie.");
            }

            var validated = Validate(candidateUtf8);
            if (!validated.Success
                || validated.CanonicalMovieUtf8 == null)
            {
                return validated;
            }

            var stored = StoreBranch(
                validated,
                suppliedBaseMovieId);
            return stored;
        }

        public MovieTimelineBranchResult EditTimeline(
            TimelineEditKind kind,
            string suppliedBaseMovieId,
            string currentBaseMovieId,
            byte[]? currentBaseMovieUtf8,
            long startTick,
            long deleteCount,
            byte[]? replacementMovieUtf8)
        {
            var expectedBase = string.IsNullOrEmpty(currentBaseMovieId)
                ? "none"
                : currentBaseMovieId;
            if (!string.Equals(
                    suppliedBaseMovieId,
                    expectedBase,
                    StringComparison.Ordinal))
            {
                return MovieTimelineBranchResult.Fail(
                    "BaseMovieChanged",
                    "Timeline edit base movie does not match current movie.");
            }

            if (currentBaseMovieUtf8 == null)
            {
                return MovieTimelineBranchResult.Fail(
                    "NoCurrentMovie",
                    "Load a canonical movie before editing its timeline.");
            }

            var baseValidation = Validate(currentBaseMovieUtf8);
            if (!baseValidation.Success
                || baseValidation.CanonicalMovieUtf8 == null
                || !string.Equals(
                    baseValidation.BranchMovieId,
                    expectedBase,
                    StringComparison.Ordinal))
            {
                return MovieTimelineBranchResult.Fail(
                    "BaseMovieInvalid",
                    "The current movie failed exact content validation.");
            }

            var baseDocument = ParseCanonical(
                baseValidation.CanonicalMovieUtf8,
                "timeline-base.hktas");
            IReadOnlyList<FrameRunCommand> replacement =
                Array.Empty<FrameRunCommand>();
            if (kind != TimelineEditKind.Delete)
            {
                if (replacementMovieUtf8 == null)
                {
                    return MovieTimelineBranchResult.Fail(
                        "ReplacementRequired",
                        "Replace and insert require a canonical input movie.");
                }

                var replacementValidation = Validate(
                    replacementMovieUtf8);
                if (!replacementValidation.Success
                    || replacementValidation.CanonicalMovieUtf8 == null)
                {
                    return MovieTimelineBranchResult.Fail(
                        replacementValidation.Code,
                        replacementValidation.Detail);
                }

                var replacementDocument = ParseCanonical(
                    replacementValidation.CanonicalMovieUtf8,
                    "timeline-replacement.hktas");
                if (replacementDocument.Commands.Any(
                        command => !(command is FrameRunCommand)))
                {
                    return MovieTimelineBranchResult.Fail(
                        "InputOnlyReplacementRequired",
                        "Timeline replacement movies may contain only frames commands.");
                }

                replacement = replacementDocument.Commands
                    .Cast<FrameRunCommand>()
                    .ToArray();
            }

            MovieTimelineEditResult edited;
            try
            {
                edited = MovieTimelineEditor.Edit(
                    baseDocument,
                    kind,
                    startTick,
                    deleteCount,
                    replacement);
            }
            catch (Exception exception)
                when (exception is ArgumentException
                      || exception is InvalidOperationException
                      || exception is OverflowException)
            {
                return MovieTimelineBranchResult.Fail(
                    "InvalidTimelineEdit",
                    exception.Message);
            }

            var candidate = new MovieCanonicalWriter().WriteUtf8(
                edited.Movie);
            var validated = Validate(candidate);
            if (!validated.Success
                || validated.CanonicalMovieUtf8 == null)
            {
                return MovieTimelineBranchResult.Fail(
                    validated.Code,
                    validated.Detail);
            }

            var stored = StoreBranch(
                validated,
                suppliedBaseMovieId);
            if (!stored.Success)
            {
                return MovieTimelineBranchResult.Fail(
                    stored.Code,
                    stored.Detail);
            }

            return new MovieTimelineBranchResult(
                true,
                "Valid",
                "Typed timeline edit was stored as an isolated branch.",
                suppliedBaseMovieId,
                stored.BranchMovieId,
                stored.CanonicalMovieUtf8,
                kind,
                edited.StartTick,
                edited.DeletedTicks,
                edited.InsertedTicks,
                edited.PreviousExpandedTicks,
                edited.ExpandedTicks,
                edited.InvalidatedCheckpoints);
        }

        private MovieBranchResult StoreBranch(
            MovieBranchResult validated,
            string parentMovieId)
        {
            if (!validated.Success
                || validated.CanonicalMovieUtf8 == null)
            {
                return validated;
            }

            var destination = BranchPath(validated.BranchMovieId);
            lock (sync)
            {
                if (File.Exists(destination))
                {
                    var existing = File.ReadAllBytes(destination);
                    if (!BytesEqual(
                            existing,
                            validated.CanonicalMovieUtf8))
                    {
                        return Fail(
                            "WorkspaceCollision",
                            "Content-addressed branch collision.");
                    }

                    RecordParent(
                        validated.BranchMovieId,
                        parentMovieId);
                    return validated;
                }

                var temporary = Path.Combine(
                    branchesRoot,
                    ".t-" + Guid.NewGuid().ToString("N"));
                try
                {
                    using (var stream = new FileStream(
                               temporary,
                               FileMode.CreateNew,
                               FileAccess.Write,
                               FileShare.None))
                    {
                        stream.Write(
                            validated.CanonicalMovieUtf8,
                            0,
                            validated.CanonicalMovieUtf8.Length);
                        stream.Flush(true);
                    }

                    File.Move(temporary, destination);
                    RecordParent(
                        validated.BranchMovieId,
                        parentMovieId);
                }
                finally
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
            }

            return validated;
        }

        private MovieDocument ParseCanonical(
            byte[] canonicalUtf8,
            string sourceName)
        {
            var source = new UTF8Encoding(false, true)
                .GetString(canonicalUtf8);
            var parsed = editor.Validate(source, sourceName);
            if (!parsed.Success || parsed.Document == null)
            {
                throw new InvalidDataException(
                    "Canonical movie could not be parsed.");
            }

            return parsed.Document;
        }

        private void RecordParent(
            string branchMovieId,
            string parentMovieId)
        {
            if (string.Equals(
                    branchMovieId,
                    parentMovieId,
                    StringComparison.Ordinal))
            {
                return;
            }

            var parent = string.IsNullOrEmpty(parentMovieId)
                ? "none"
                : parentMovieId;
            var path = ParentPath(branchMovieId);
            var parents = File.Exists(path)
                ? new HashSet<string>(
                    File.ReadAllLines(path),
                    StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            if (!parents.Add(parent))
            {
                return;
            }

            var temporary = Path.Combine(
                branchesRoot,
                ".p-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllLines(
                    temporary,
                    parents.OrderBy(
                        value => value,
                        StringComparer.Ordinal),
                    new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        public byte[] ReadBranch(string branchMovieId)
        {
            if (!MovieProtocolV1.IsLowerSha256(branchMovieId))
            {
                throw new InvalidDataException(
                    "Branch movie ID is invalid.");
            }

            var path = FindBranchPath(branchMovieId, lifecycle: false);
            if (path == null)
            {
                throw new FileNotFoundException(
                    "Movie branch does not exist.");
            }

            var bytes = File.ReadAllBytes(path);
            var validation = Validate(bytes);
            if (!validation.Success
                || validation.BranchMovieId != branchMovieId
                || validation.CanonicalMovieUtf8 == null
                || !BytesEqual(
                    bytes,
                    validation.CanonicalMovieUtf8))
            {
                throw new InvalidDataException(
                    "Stored movie branch failed content validation.");
            }

            return bytes;
        }

        private string BranchPath(string movieId)
        {
            return Path.Combine(branchesRoot, movieId + ".hktas");
        }

        private string ParentPath(string movieId)
        {
            return Path.Combine(branchesRoot, movieId + ".parents");
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            var difference = 0;
            for (var index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
        }

        private static MovieBranchResult Fail(
            string code,
            string detail)
        {
            return new MovieBranchResult(
                false,
                code,
                detail,
                string.Empty,
                null,
                0);
        }
    }

    public sealed class MovieTimelineBranchResult
    {
        private readonly IReadOnlyList<string> invalidatedCheckpoints;

        public MovieTimelineBranchResult(
            bool success,
            string code,
            string detail,
            string parentMovieId,
            string branchMovieId,
            byte[]? canonicalMovieUtf8,
            TimelineEditKind kind,
            long startTick,
            long deletedTicks,
            long insertedTicks,
            long previousExpandedTicks,
            long expandedTicks,
            IEnumerable<string> invalidatedCheckpoints)
        {
            Success = success;
            Code = code;
            Detail = detail;
            ParentMovieId = parentMovieId;
            BranchMovieId = branchMovieId;
            CanonicalMovieUtf8 = canonicalMovieUtf8;
            Kind = kind;
            StartTick = startTick;
            DeletedTicks = deletedTicks;
            InsertedTicks = insertedTicks;
            PreviousExpandedTicks = previousExpandedTicks;
            ExpandedTicks = expandedTicks;
            this.invalidatedCheckpoints =
                new List<string>(invalidatedCheckpoints);
        }

        public bool Success { get; }
        public string Code { get; }
        public string Detail { get; }
        public string ParentMovieId { get; }
        public string BranchMovieId { get; }
        public byte[]? CanonicalMovieUtf8 { get; }
        public TimelineEditKind Kind { get; }
        public long StartTick { get; }
        public long DeletedTicks { get; }
        public long InsertedTicks { get; }
        public long TickDelta => InsertedTicks - DeletedTicks;
        public long PreviousExpandedTicks { get; }
        public long ExpandedTicks { get; }
        public IReadOnlyList<string> InvalidatedCheckpoints =>
            invalidatedCheckpoints;

        public static MovieTimelineBranchResult Fail(
            string code,
            string detail)
        {
            return new MovieTimelineBranchResult(
                false,
                code,
                detail,
                string.Empty,
                string.Empty,
                null,
                TimelineEditKind.Replace,
                0,
                0,
                0,
                0,
                0,
                Array.Empty<string>());
        }
    }
}
