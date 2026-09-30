using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace HollowKnightTAS.Companion.Services;

public sealed class DiagnosticLogExporter
{
    public static string LocalRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HollowKnightTAS");
    public static string GameRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Team Cherry", "Hollow Knight");
    private static readonly string[] GameLogs = { "ModLog.txt", "ModLog-prev.txt", "Player.log", "Player-prev.log" };
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".jsonl", ".json", ".sha256", ".txt", ".log", ".csv" };

    // Inspect only known diagnostic trees; never include save slots, boot descriptors or credentials.
    public static (string Path, int Files, int Warnings) Export(string destination, string? localRoot = null, string? gameRoot = null)
    {
        localRoot ??= LocalRoot;
        gameRoot ??= GameRoot;
        var warnings = new List<string>();
        var files = new List<object>();
        var output = Path.Combine(destination, "HollowKnightTAS-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
        var temporary = output + ".partial";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                void Add(string source, string name)
                {
                    if (!File.Exists(source)) return;
                    FileStream input;
                    try
                    {
                        RejectLinks(source);
                        input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { warnings.Add(name + ": " + ex.Message); return; }
                    using (input)
                    {
                        var length = input.Length;
                        var entry = zip.CreateEntry(name.Replace('\\', '/'), CompressionLevel.Fastest);
                        using var target = entry.Open();
                        var buffer = new byte[81920];
                        long copied = 0;
                        while (copied < length)
                        {
                            int read;
                            try { read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, length - copied)); }
                            catch (IOException ex) { warnings.Add(name + ": " + ex.Message); break; }
                            if (read == 0) break;
                            target.Write(buffer, 0, read);
                            copied += read;
                        }
                        files.Add(new { entry = entry.FullName, bytes = copied });
                        if (copied != length) warnings.Add(name + ": file changed during export; partial snapshot.");
                    }
                }
                void Tree(string root, string prefix, Func<string, bool>? filter = null)
                {
                    if (!Directory.Exists(root)) return;
                    string[] children;
                    string[] sources;
                    try
                    {
                        RejectLinks(root);
                        sources = Directory.GetFiles(root);
                        children = Directory.GetDirectories(root);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { warnings.Add(prefix + ": " + ex.Message); return; }
                    foreach (var path in sources)
                        if (IsDiagnosticFile(path) && (filter == null || filter(path)))
                            Add(path, prefix + "/" + Path.GetFileName(path));
                    foreach (var child in children)
                        Tree(child, prefix + "/" + Path.GetFileName(child), filter);
                }
                foreach (var name in GameLogs) Add(Path.Combine(gameRoot, name), "game/" + name);
                Tree(Path.Combine(gameRoot, "Old ModLogs"), "game/Old ModLogs",
                    path => Path.GetFileName(path).StartsWith("ModLog ", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase));
                Tree(Path.Combine(gameRoot, "HollowKnightTAS", "sessions"), "game/sessions");
                Tree(Path.Combine(gameRoot, "HollowKnightTAS", "diagnostics"), "game/diagnostics");
                foreach (var name in new[] { "performance", "diagnostic-history", "application" }) Tree(Path.Combine(localRoot, name), "studio/" + name);
                var shadows = Path.Combine(localRoot, "save-shadows");
                if (Directory.Exists(shadows))
                {
                    string[] runs = Array.Empty<string>();
                    try { RejectLinks(shadows); runs = Directory.GetDirectories(shadows); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warnings.Add("runs: " + ex.Message); }
                    foreach (var run in runs)
                        foreach (var name in new[] { "sessions", "diagnostics" })
                            Tree(Path.Combine(run, "HollowKnightTAS", name), "runs/" + Path.GetFileName(run) + "/" + name);
                }
                Tree(Path.Combine(localRoot, "cold-restore", "store"), "studio/cold-restore",
                    path => Path.GetFileName(path) is "failure.txt" or "slot-recovery-failure.txt");
                using var manifest = new StreamWriter(zip.CreateEntry("export-report.json").Open());
                manifest.Write(JsonSerializer.Serialize(new { exportedUtc = DateTime.UtcNow, studioVersion = typeof(DiagnosticLogExporter).Assembly.GetName().Version?.ToString(), scope = "All retained diagnostic sessions. Saves, movies and credentials excluded. Live files are snapshots.", files, warnings }, new JsonSerializerOptions { WriteIndented = true }));
            }
            File.Move(temporary, output);
            return (output, files.Count, warnings.Count);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void PreserveGameLogs()
    {
        lock (DiagnosticHistoryRetention.SyncRoot)
        try
        {
            var directory = Path.Combine(LocalRoot, "diagnostic-history", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N")[..8]);
            foreach (var name in GameLogs)
            {
                var source = Path.Combine(GameRoot, name);
                if (!File.Exists(source)) continue;
                RejectLinks(source);
                Directory.CreateDirectory(directory);
                using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var output = File.Create(Path.Combine(directory, name));
                var remaining = input.Length;
                var buffer = new byte[81920];
                while (remaining > 0)
                {
                    var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read == 0) break;
                    output.Write(buffer, 0, read);
                    remaining -= read;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(ex); }
        finally
        {
            try { DiagnosticHistoryRetention.Apply(LocalRoot); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(ex); }
        }
    }

    private static bool IsDiagnosticFile(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".incomplete", StringComparison.OrdinalIgnoreCase))
            path = Path.GetFileNameWithoutExtension(path);
        return Extensions.Contains(Path.GetExtension(path));
    }

    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked diagnostic paths are excluded.");
    }
}
