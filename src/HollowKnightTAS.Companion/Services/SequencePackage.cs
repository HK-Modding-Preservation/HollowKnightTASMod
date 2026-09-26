using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.ReplaySave;

namespace HollowKnightTAS.Companion.Services;

/// <summary>Immutable, complete file set for slots 1–4, including absent files.</summary>
public sealed class InitialSaveSnapshot
{
    private readonly Dictionary<string, byte[]> files = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> Hashes { get; }
    public string Id { get; }

    public InitialSaveSnapshot(IEnumerable<KeyValuePair<string, byte[]>> source)
    {
        long total = 0;
        foreach (var pair in source)
        {
            var name = pair.Key.ToLowerInvariant();
            if (!ProtectedSaveDescriptor.IsSlotFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || files.ContainsKey(name) || pair.Value == null
                || pair.Value.LongLength > ProtectedSaveSession.MaximumFileBytes
                || (total += pair.Value.LongLength) > ProtectedSaveSession.MaximumTotalBytes
                || files.Count >= ProtectedSaveSession.MaximumFiles)
                throw new InvalidDataException("初始存档文件无效或超过限制。");
            files.Add(name, (byte[])pair.Value.Clone());
        }
        var hashes = files.OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToDictionary(p => p.Key, p => Sha256Utility.ComputeHex(p.Value), StringComparer.OrdinalIgnoreCase);
        Hashes = new ReadOnlyDictionary<string, string>(hashes);
        Id = Sha256Utility.ComputeHex(JsonSerializer.SerializeToUtf8Bytes(hashes));
    }

    internal IEnumerable<KeyValuePair<string, byte[]>> CopyFiles()
        => files.Select(p => new KeyValuePair<string, byte[]>(p.Key, (byte[])p.Value.Clone()));

    internal void WriteToNewShadow(string directory)
    {
        foreach (var pair in files)
        {
            using var output = new FileStream(Path.Combine(directory, pair.Key), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write(pair.Value);
            output.Flush(true);
        }
    }
}

public sealed record SequenceFile(string Movie, InitialSaveSnapshot? InitialSaves);

/// <summary>Portable sequence archive; never extracts archive paths to disk.</summary>
public static class SequencePackage
{
    public const string Extension = ".hktaspack";
    public const int MaximumMovieBytes = 16 * 1024 * 1024;
    private const int MaximumManifestBytes = 128 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static SequenceFile Read(string path)
    {
        using var input = File.OpenRead(path);
        if (!path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            if (input.Length > MaximumMovieBytes) throw new InvalidDataException("序列文件超过 16 MiB。");
            using var reader = new StreamReader(input, Utf8);
            return new SequenceFile(reader.ReadToEnd(), null);
        }
        if (input.Length > ProtectedSaveSession.MaximumTotalBytes + MaximumMovieBytes + 1024 * 1024)
            throw new InvalidDataException("序列包过大。");
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        if (archive.Entries.Count > ProtectedSaveSession.MaximumFiles + 2)
            throw new InvalidDataException("序列包文件数量过多。");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
            if (!entries.TryAdd(entry.FullName, entry)) throw new InvalidDataException("序列包包含重复文件。");
        byte[] ReadEntry(string name, long maximum)
        {
            if (!entries.Remove(name, out var entry) || entry.Length > maximum || entry.Length < 0)
                throw new InvalidDataException("序列包缺少文件或文件过大：" + name);
            using var stream = entry.Open();
            var bytes = new byte[checked((int)entry.Length)];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw new InvalidDataException("序列包文件长度不匹配。");
            return bytes;
        }
        var manifest = JsonSerializer.Deserialize<Manifest>(ReadEntry("manifest.json", MaximumManifestBytes))
            ?? throw new InvalidDataException("序列包清单无效。");
        if (manifest.Version != 1 || manifest.Format != "HK-TAS-Sequence" || manifest.Slots == null
            || !manifest.Slots.SequenceEqual(new[] { 1, 2, 3, 4 }) || manifest.Files == null
            || manifest.Files.Count > ProtectedSaveSession.MaximumFiles)
            throw new InvalidDataException("序列包版本或四槽清单无效。");
        var movieBytes = ReadEntry("movie.hktas", MaximumMovieBytes);
        if (Sha256Utility.ComputeHex(movieBytes) != manifest.MovieSha256) throw new InvalidDataException("序列内容校验失败。");
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var item in manifest.Files)
        {
            if (item == null || !ProtectedSaveDescriptor.IsSlotFileName(item.Name) || item.Length < 0
                || item.Length > ProtectedSaveSession.MaximumFileBytes
                || (total += item.Length) > ProtectedSaveSession.MaximumTotalBytes)
                throw new InvalidDataException("初始存档清单无效。");
            var bytes = ReadEntry("initial-saves/" + item.Name, item.Length);
            if (bytes.LongLength != item.Length || Sha256Utility.ComputeHex(bytes) != item.Sha256 || !files.TryAdd(item.Name, bytes))
                throw new InvalidDataException("初始存档校验失败：" + item.Name);
        }
        if (entries.Count != 0) throw new InvalidDataException("序列包包含未声明文件。");
        var snapshot = new InitialSaveSnapshot(files);
        if (snapshot.Id != manifest.InitialSavesId) throw new InvalidDataException("初始存档集合校验失败。");
        return new SequenceFile(Utf8.GetString(movieBytes), snapshot);
    }

    public static async Task WriteAsync(string path, string movie, InitialSaveSnapshot? snapshot)
    {
        var isPackage = path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
        if (isPackage != (snapshot != null)) throw new InvalidDataException("绑定存档的序列必须保存为 .hktaspack；未绑定序列使用 .hktas。");
        var movieBytes = Utf8.GetBytes(movie);
        if (movieBytes.Length > MaximumMovieBytes) throw new InvalidDataException("序列文件超过 16 MiB。");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (snapshot == null) await output.WriteAsync(movieBytes);
                else
                {
                    using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
                    void Write(string name, byte[] bytes)
                    {
                        using var entry = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
                        entry.Write(bytes);
                    }
                    var manifest = new Manifest { InitialSavesId = snapshot.Id, MovieSha256 = Sha256Utility.ComputeHex(movieBytes) };
                    foreach (var pair in snapshot.CopyFiles().OrderBy(p => p.Key, StringComparer.Ordinal))
                    {
                        manifest.Files.Add(new SaveFile { Name = pair.Key, Length = pair.Value.LongLength, Sha256 = snapshot.Hashes[pair.Key] });
                        Write("initial-saves/" + pair.Key, pair.Value);
                    }
                    Write("movie.hktas", movieBytes);
                    Write("manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest));
                }
                output.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed class Manifest
    {
        public string Format { get; set; } = "HK-TAS-Sequence";
        public int Version { get; set; } = 1;
        public int[] Slots { get; set; } = new[] { 1, 2, 3, 4 };
        public string InitialSavesId { get; set; } = "";
        public string MovieSha256 { get; set; } = "";
        public List<SaveFile> Files { get; set; } = new();
    }
    private sealed class SaveFile
    {
        public string Name { get; set; } = "";
        public long Length { get; set; }
        public string Sha256 { get; set; } = "";
    }
}
