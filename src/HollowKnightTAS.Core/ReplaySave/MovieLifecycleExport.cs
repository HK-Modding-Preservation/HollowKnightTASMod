using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Core.ReplaySave
{
    public sealed class MovieLifecycleExport
    {
        public const int MaximumBytes = 128 * 1024 * 1024;
        public const int ChunkBytes = 384 * 1024;
        private readonly byte[] encoded;
        private MovieLifecycleExport(byte[] encoded) { this.encoded = encoded; Sha256 = Sha256Utility.ComputeHex(encoded); }
        public string Sha256 { get; }
        public int Length => encoded.Length;
        public int ChunkCount => (Length + ChunkBytes - 1) / ChunkBytes;
        public byte[] GetChunk(int index)
        {
            if (index < 0 || index >= ChunkCount) throw new ArgumentOutOfRangeException(nameof(index));
            var start = checked(index * ChunkBytes);
            var bytes = new byte[Math.Min(ChunkBytes, Length - start)];
            Array.Copy(encoded, start, bytes, 0, bytes.Length);
            return bytes;
        }

        public static MovieLifecycleExportData Decode(byte[] bytes, string expectedSha256)
        {
            if (bytes == null || bytes.Length > MaximumBytes || Sha256Utility.ComputeHex(bytes) != expectedSha256)
                throw new InvalidDataException("Lifecycle export hash or size mismatch.");
            var reader = new ReplaySaveBinaryReader(bytes, MaximumBytes);
            reader.RequireMagic("HKLE");
            if (reader.ReadInt32() != 1) throw new InvalidDataException("Unsupported lifecycle export version.");
            var movieBytes = reader.ReadBytes(32 * 1024 * 1024, "movie");
            var parsed = new MovieParser().Parse(new StringReader(new UTF8Encoding(false, true).GetString(movieBytes)), "export");
            if (!parsed.Success || parsed.Document == null
                || !new MovieValidator().Validate(parsed.Document, MovieValidationContext.CreateDefault()).Success)
                throw new InvalidDataException("Invalid exported movie.");
            var baseline = reader.ReadBytes(BaselineBundle.MaximumBundleBytes, "baseline");
            var log = ReplayLifecycleLog.Deserialize(reader.ReadBytes(ReplayLifecycleLog.MaximumBytes, "lifecycle"));
            var count = reader.ReadInt32();
            if (count < 0 || count > ReplayLifecycleLog.MaximumRecords * 2)
                throw new InvalidDataException("Invalid export object count.");
            var objects = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            long total = 0;
            for (var i = 0; i < count; i++)
            {
                var hash = reader.ReadString(64, "object hash");
                var value = reader.ReadBytes(ReplayLifecycleLog.MaximumSlotBytes, "object");
                total += value.Length;
                if (total > 64L * 1024 * 1024 || objects.ContainsKey(hash))
                    throw new InvalidDataException("Export object budget or uniqueness violation.");
                objects.Add(hash, value);
            }
            reader.RequireEnd();
            var canonical = Create(parsed.Document, baseline, log, objects);
            if (canonical.Sha256 != expectedSha256) throw new InvalidDataException("Noncanonical lifecycle export.");
            return new MovieLifecycleExportData(parsed.Document, baseline, log, objects);
        }

        public static MovieLifecycleExport Create(MovieDocument movie, byte[] baselineBytes,
            ReplayLifecycleLog lifecycle, IReadOnlyDictionary<string, byte[]> objects)
        {
            var baseline = (byte[])baselineBytes.Clone();
            if (baseline.Length > BaselineBundle.MaximumBundleBytes
                || Sha256Utility.ComputeHex(baseline) != lifecycle.RootBaselineSha256)
                throw new InvalidDataException("Lifecycle export baseline mismatch.");
            var bundle = BaselineBundleCodec.Deserialize(baseline);
            if (movie.Header.BaselineId != bundle.BaselineId || movie.Header.BaselineSha256 != bundle.BaselineSemanticSha256)
                throw new InvalidDataException("Exported movie does not belong to the captured baseline.");
            if (!lifecycle.IsCompleted) throw new InvalidDataException("Lifecycle export is unfinished.");
            lifecycle.VerifyInputPrefixes(movie);
            var captured = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            long total = 0;
            foreach (var hash in lifecycle.Records.Where(x => x.Kind == ReplayLifecycleKind.LoadSlot)
                .SelectMany(x => new[] { x.SlotObjectSha256, x.ModdedSlotObjectSha256 })
                .Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Distinct(StringComparer.Ordinal))
            {
                if (!objects.TryGetValue(hash, out var bytes) || bytes == null
                    || bytes.Length > ReplayLifecycleLog.MaximumSlotBytes)
                    throw new InvalidDataException("Lifecycle export dependency unavailable.");
                total += bytes.Length;
                if (total > 64L * 1024 * 1024) throw new InvalidDataException("Lifecycle export object budget exceeded.");
                captured.Add(hash, (byte[])bytes.Clone());
            }
            lifecycle.VerifySlotObjects(hash => captured.TryGetValue(hash, out var bytes) ? bytes : null);
            using var writer = new ReplaySaveBinaryWriter();
            writer.WriteMagic("HKLE"); writer.WriteInt32(1);
            writer.WriteBytes(new MovieCanonicalWriter().WriteUtf8(movie), 32 * 1024 * 1024);
            writer.WriteBytes(baseline, BaselineBundle.MaximumBundleBytes);
            writer.WriteBytes(lifecycle.Serialize(), ReplayLifecycleLog.MaximumBytes);
            writer.WriteInt32(captured.Count);
            foreach (var item in captured)
            { writer.WriteString(item.Key, 64); writer.WriteBytes(item.Value, ReplayLifecycleLog.MaximumSlotBytes); }
            var encoded = writer.ToArray();
            if (encoded.Length > MaximumBytes) throw new InvalidDataException("Lifecycle export exceeds byte limit.");
            return new MovieLifecycleExport(encoded);
        }
    }

    public sealed class MovieLifecycleExportData
    {
        private readonly byte[] baseline;
        private readonly Dictionary<string, byte[]> objects;
        internal MovieLifecycleExportData(MovieDocument movie, byte[] baseline, ReplayLifecycleLog lifecycle,
            Dictionary<string, byte[]> objects)
        { Movie = movie; this.baseline = baseline; Lifecycle = lifecycle; this.objects = objects; }
        public MovieDocument Movie { get; }
        public ReplayLifecycleLog Lifecycle { get; }
        public byte[] CopyBaselineBytes() => (byte[])baseline.Clone();
        public IReadOnlyDictionary<string, byte[]> CopySlotObjects() =>
            objects.ToDictionary(x => x.Key, x => (byte[])x.Value.Clone(), StringComparer.Ordinal);
    }
}
