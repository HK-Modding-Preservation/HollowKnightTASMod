using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.ReplaySave;

namespace HollowKnightTAS.Companion.Automation
{
    public sealed class LifecycleMovieBranch
    {
        private readonly Dictionary<string, byte[]> objects;
        private readonly byte[]? baseline;
        internal LifecycleMovieBranch(string id, ReplayLifecycleTimelineEditResult plan,
            IEnumerable<KeyValuePair<string, byte[]>> objects, byte[]? baseline)
        {
            BranchId = id;
            Plan = plan;
            this.objects = objects.ToDictionary(x => x.Key, x => (byte[])x.Value.Clone(), StringComparer.Ordinal);
            this.baseline = baseline == null ? null : (byte[])baseline.Clone();
        }
        public string BranchId { get; }
        public ReplayLifecycleTimelineEditResult Plan { get; }
        public bool HasRootBaseline => baseline != null;
        public ReplayLifecycleExecutionPlan CreateExecutionPlan() =>
            ReplayLifecycleExecutionPlan.FromEdit(BranchId, Plan, CopyRootBaseline(), CopySlotObjects());
        public byte[] CopyRootBaseline() => baseline == null
            ? throw new InvalidOperationException("Legacy lifecycle branch has no root baseline; recapture its source.")
            : (byte[])baseline.Clone();
        public IReadOnlyDictionary<string, byte[]> CopySlotObjects() =>
            objects.ToDictionary(x => x.Key, x => (byte[])x.Value.Clone(), StringComparer.Ordinal);
    }

    // Separate extension and identity from frame-only branches: old callers must
    // not accidentally read an edited lifecycle branch as a plain .hktas movie.
    public sealed class LifecycleMovieBranchStore
    {
        private const int MaximumMovieBytes = 32 * 1024 * 1024;
        private const int MaximumObjectsBytes = 64 * 1024 * 1024;
        private const int MaximumArchiveBytes = 192 * 1024 * 1024;
        private readonly string root;
        private readonly MovieEditorService editor = new MovieEditorService();
        private sealed record FurtherEdit(TimelineEditKind Kind, long Start, long Count, byte[] Replacement);
        private const int MaximumFurtherEdits = 4096;

        public LifecycleMovieBranchStore(string root)
        {
            this.root = Path.GetFullPath(root);
            Directory.CreateDirectory(this.root);
        }

        public LifecycleMovieBranch Store(byte[] sourceMovie, ReplayLifecycleLog lifecycle,
            IReadOnlyDictionary<string, byte[]> objects, TimelineEditKind kind,
            long startTick, long deleteCount, byte[] replacementMovie, byte[]? rootBaseline = null)
        {
            // Canonicalization also clones caller buffers before validation and publication.
            var source = CanonicalMovie(sourceMovie);
            var replacement = CanonicalMovie(replacementMovie);
            var logBytes = lifecycle.Serialize();
            var log = ReplayLifecycleLog.Deserialize(logBytes);
            var baseline = rootBaseline == null ? null : (byte[])rootBaseline.Clone();
            if (baseline != null) VerifyBaseline(baseline, source, log);
            var dependencies = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            long total = 0;
            foreach (var record in log.Records)
            {
                if (record.Kind != ReplayLifecycleKind.LoadSlot) continue;
                if (record.ModdedSlotObjectSha256 == null)
                    throw new InvalidDataException("Lifecycle branch requires known mod settings capture.");
                Add(record.SlotObjectSha256);
                if (record.ModdedSlotObjectSha256.Length != 0) Add(record.ModdedSlotObjectSha256);
            }
            void Add(string hash)
            {
                if (dependencies.ContainsKey(hash)) return;
                if (!objects.TryGetValue(hash, out var bytes) || bytes == null
                    || bytes.Length > ReplayLifecycleLog.MaximumSlotBytes)
                    throw new InvalidDataException("Lifecycle dependency is missing or too large.");
                total = checked(total + bytes.Length);
                if (total > MaximumObjectsBytes) throw new InvalidDataException("Lifecycle branch object budget exceeded.");
                dependencies.Add(hash, (byte[])bytes.Clone());
            }
            log.VerifySlotObjects(hash => dependencies.TryGetValue(hash, out var bytes) ? bytes : null);
            var plan = BuildPlan(source, log, kind, startTick, deleteCount, replacement);
            var archive = Encode(source, logBytes, dependencies, kind, startTick, deleteCount, replacement, baseline);
            return Publish(archive, plan, dependencies, baseline);
        }

        public LifecycleMovieBranch Edit(string parentId, TimelineEditKind kind, long startTick,
            long deleteCount, byte[] replacementMovie)
        {
            var parent = Read(parentId);
            // Validate and reconstruct from the immutable source plus all recipes,
            // never manufacture a Completed log from mapped operations.
            var parentBytes = ReadBounded(Path.Combine(root, parentId + ".hklbranch"));
            if (Sha256Utility.ComputeHex(parentBytes) != parentId)
                throw new InvalidDataException("Lifecycle parent changed during edit.");
            using var document = JsonDocument.Parse(parentBytes);
            var json = document.RootElement;
            var edits = ReadFurtherEdits(json);
            if (edits.Count >= MaximumFurtherEdits) throw new InvalidDataException("Lifecycle edit count exceeded.");
            var replacement = CanonicalMovie(replacementMovie);
            var plan = ApplyFurtherEdit(parent.Plan, new FurtherEdit(kind, startTick, deleteCount, replacement));
            edits.Add(new FurtherEdit(kind, startTick, deleteCount, replacement));
            var dependencies = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var item in parent.CopySlotObjects()) dependencies.Add(item.Key, item.Value);
            var baseline = parent.HasRootBaseline ? parent.CopyRootBaseline() : null;
            var archive = Encode(json.GetProperty("sourceMovie").GetBytesFromBase64(),
                json.GetProperty("sourceLifecycle").GetBytesFromBase64(), dependencies,
                (TimelineEditKind)json.GetProperty("kind").GetInt32(), json.GetProperty("startTick").GetInt64(),
                json.GetProperty("deleteCount").GetInt64(), json.GetProperty("replacementMovie").GetBytesFromBase64(),
                baseline, edits);
            return Publish(archive, plan, dependencies, baseline);
        }

        private LifecycleMovieBranch Publish(byte[] archive, ReplayLifecycleTimelineEditResult plan,
            SortedDictionary<string, byte[]> dependencies, byte[]? baseline)
        {
            var id = Sha256Utility.ComputeHex(archive);
            var destination = Path.Combine(root, id + ".hklbranch");
            if (File.Exists(destination))
            {
                if (!ReadBounded(destination).SequenceEqual(archive))
                    throw new InvalidDataException("Lifecycle branch content collision.");
                return new LifecycleMovieBranch(id, plan, dependencies, baseline);
            }
            var temporary = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(archive, 0, archive.Length); stream.Flush(true); }
                try { File.Move(temporary, destination); }
                catch (IOException) when (File.Exists(destination))
                {
                    if (!ReadBounded(destination).SequenceEqual(archive)) throw;
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return new LifecycleMovieBranch(id, plan, dependencies, baseline);
        }

        public LifecycleMovieBranch Read(string branchId)
        {
            if (!MovieProtocolV1.IsLowerSha256(branchId)) throw new InvalidDataException("Invalid lifecycle branch ID.");
            var bytes = ReadBounded(Path.Combine(root, branchId + ".hklbranch"));
            return Decode(bytes, branchId);
        }

        public LifecycleMovieBranch ImportFile(string path, string branchId)
        {
            var bytes = ReadBounded(path);
            var branch = Decode(bytes, branchId);
            var objects = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var item in branch.CopySlotObjects()) objects.Add(item.Key, item.Value);
            return Publish(bytes, branch.Plan, objects, branch.HasRootBaseline ? branch.CopyRootBaseline() : null);
        }

        private LifecycleMovieBranch Decode(byte[] bytes, string branchId)
        {
            if (!MovieProtocolV1.IsLowerSha256(branchId)) throw new InvalidDataException("Invalid lifecycle branch ID.");
            if (Sha256Utility.ComputeHex(bytes) != branchId) throw new InvalidDataException("Lifecycle branch hash mismatch.");
            using var document = JsonDocument.Parse(bytes);
            var json = document.RootElement;
            var version = json.GetProperty("schemaVersion").GetInt32();
            if (version != 1 && version != 2 && version != 3)
                throw new InvalidDataException("Unsupported lifecycle branch schema.");
            var source = json.GetProperty("sourceMovie").GetBytesFromBase64();
            var logBytes = json.GetProperty("sourceLifecycle").GetBytesFromBase64();
            var replacement = json.GetProperty("replacementMovie").GetBytesFromBase64();
            var log = ReplayLifecycleLog.Deserialize(logBytes);
            var baseline = json.TryGetProperty("rootBaseline", out var baselineProperty)
                ? baselineProperty.GetBytesFromBase64() : null;
            if (version == 2 && baseline == null) throw new InvalidDataException("Missing root baseline.");
            if (baseline != null) VerifyBaseline(baseline, source, log);
            var objects = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            long total = 0;
            foreach (var item in json.GetProperty("objects").EnumerateObject())
            {
                var value = item.Value.GetBytesFromBase64();
                total = checked(total + value.Length);
                if (!MovieProtocolV1.IsLowerSha256(item.Name) || value.Length > ReplayLifecycleLog.MaximumSlotBytes
                    || total > MaximumObjectsBytes || Sha256Utility.ComputeHex(value) != item.Name)
                    throw new InvalidDataException("Invalid lifecycle branch object.");
                objects.Add(item.Name, value);
            }
            if (log.Records.Any(x => x.Kind == ReplayLifecycleKind.LoadSlot && x.ModdedSlotObjectSha256 == null))
                throw new InvalidDataException("Unknown mod settings capture.");
            log.VerifySlotObjects(hash => objects.TryGetValue(hash, out var value) ? value : null);
            var kind = (TimelineEditKind)json.GetProperty("kind").GetInt32();
            var start = json.GetProperty("startTick").GetInt64();
            var count = json.GetProperty("deleteCount").GetInt64();
            var furtherEdits = ReadFurtherEdits(json);
            if (!Encode(source, logBytes, objects, kind, start, count, replacement, baseline,
                version == 3 ? furtherEdits : null).SequenceEqual(bytes))
                throw new InvalidDataException("Noncanonical lifecycle branch.");
            var plan = BuildPlan(source, log, kind, start, count, replacement);
            foreach (var edit in furtherEdits) plan = ApplyFurtherEdit(plan, edit);
            return new LifecycleMovieBranch(branchId, plan, objects, baseline);
        }

        private static List<FurtherEdit> ReadFurtherEdits(JsonElement json)
        {
            var result = new List<FurtherEdit>();
            if (json.GetProperty("schemaVersion").GetInt32() != 3) return result;
            var edits = json.GetProperty("furtherEdits");
            if (edits.GetArrayLength() == 0 || edits.GetArrayLength() > MaximumFurtherEdits)
                throw new InvalidDataException("Invalid lifecycle edit count.");
            foreach (var edit in edits.EnumerateArray())
                result.Add(new FurtherEdit((TimelineEditKind)edit.GetProperty("kind").GetInt32(),
                    edit.GetProperty("startTick").GetInt64(), edit.GetProperty("deleteCount").GetInt64(),
                    edit.GetProperty("replacementMovie").GetBytesFromBase64()));
            return result;
        }

        private ReplayLifecycleTimelineEditResult ApplyFurtherEdit(ReplayLifecycleTimelineEditResult previous, FurtherEdit edit)
        {
            var replacement = Parse(edit.Replacement);
            if (replacement.Commands.Any(x => !(x is FrameRunCommand)))
                throw new InvalidDataException("Replacement must contain only input frames.");
            return ReplayLifecycleTimelineEditor.Edit(previous, edit.Kind, edit.Start, edit.Count,
                replacement.Commands.Cast<FrameRunCommand>());
        }

        private void VerifyBaseline(byte[] baseline, byte[] source, ReplayLifecycleLog log)
        {
            if (baseline.Length > BaselineBundle.MaximumBundleBytes
                || Sha256Utility.ComputeHex(baseline) != log.RootBaselineSha256)
                throw new InvalidDataException("Lifecycle branch root baseline hash mismatch.");
            var bundle = BaselineBundleCodec.Deserialize(baseline);
            var movie = Parse(source);
            if (movie.Header.BaselineId != bundle.BaselineId || movie.Header.BaselineSha256 != bundle.BaselineSemanticSha256)
                throw new InvalidDataException("Lifecycle branch source and root baseline disagree.");
        }

        private ReplayLifecycleTimelineEditResult BuildPlan(byte[] source, ReplayLifecycleLog log,
            TimelineEditKind kind, long start, long count, byte[] replacement)
        {
            var movie = Parse(source);
            var replacementDocument = Parse(replacement);
            if (replacementDocument.Commands.Any(x => !(x is FrameRunCommand)))
                throw new InvalidDataException("Replacement must contain only input frames.");
            if (kind == TimelineEditKind.Replace && start == 0 && count == 0 && replacementDocument.Commands.Count == 0)
                return ReplayLifecycleTimelineEditor.CaptureUnedited(movie, log);
            return ReplayLifecycleTimelineEditor.Edit(movie, log, kind, start, count,
                replacementDocument.Commands.Cast<FrameRunCommand>());
        }

        private MovieDocument Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaximumMovieBytes)
                throw new InvalidDataException("Movie size is outside branch bounds.");
            var result = editor.Validate(new UTF8Encoding(false, true).GetString(bytes));
            if (!result.Success || result.Document == null) throw new InvalidDataException("Invalid branch movie.");
            return result.Document;
        }
        private byte[] CanonicalMovie(byte[] bytes) => new MovieCanonicalWriter().WriteUtf8(Parse(bytes));
        private static byte[] ReadBounded(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumArchiveBytes) throw new InvalidDataException("Lifecycle branch is too large.");
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            return bytes;
        }
        private static byte[] Encode(byte[] source, byte[] log, SortedDictionary<string, byte[]> objects,
            TimelineEditKind kind, long start, long count, byte[] replacement, byte[]? baseline,
            List<FurtherEdit>? furtherEdits = null)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("schemaVersion", furtherEdits != null ? 3 : baseline == null ? 1 : 2);
                if (baseline != null) writer.WriteBase64String("rootBaseline", baseline);
                writer.WriteBase64String("sourceMovie", source);
                writer.WriteBase64String("sourceLifecycle", log);
                writer.WriteNumber("kind", (int)kind);
                writer.WriteNumber("startTick", start);
                writer.WriteNumber("deleteCount", count);
                writer.WriteBase64String("replacementMovie", replacement);
                if (furtherEdits != null)
                {
                    writer.WriteStartArray("furtherEdits");
                    foreach (var edit in furtherEdits)
                    {
                        writer.WriteStartObject();
                        writer.WriteNumber("kind", (int)edit.Kind);
                        writer.WriteNumber("startTick", edit.Start);
                        writer.WriteNumber("deleteCount", edit.Count);
                        writer.WriteBase64String("replacementMovie", edit.Replacement);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                writer.WriteStartObject("objects");
                foreach (var item in objects) writer.WriteBase64String(item.Key, item.Value);
                writer.WriteEndObject(); writer.WriteEndObject();
            }
            if (stream.Length > MaximumArchiveBytes) throw new InvalidDataException("Lifecycle branch is too large.");
            return stream.ToArray();
        }
    }
}
