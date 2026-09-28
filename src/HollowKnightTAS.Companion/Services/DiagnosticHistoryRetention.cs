using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HollowKnightTAS.Companion.Services;

public static class DiagnosticHistoryRetention
{
    public const long MaximumBytes = 256L * 1024 * 1024;
    public const int RetentionDays = 7;
    internal static readonly object SyncRoot = new();

    // Only disposable copies owned by Studio. Never prune Runtime evidence or save shadows.
    public static (int Deleted, long RemainingBytes, int Failures) Apply(string localRoot,
        DateTime? utcNow = null, long maximumBytes = MaximumBytes)
    {
        if (maximumBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        lock (SyncRoot)
        {
            var root = Path.GetFullPath(localRoot);
            var files = new List<FileInfo>();
            var snapshotDirectories = new List<string>();
            var failures = 0;
            var deleted = 0;
            var cutoff = (utcNow ?? DateTime.UtcNow).AddDays(-RetentionDays);
            void ReadFiles(string directory, Func<string, bool> include)
            {
                if (!Directory.Exists(directory)) return;
                try
                {
                    Validate(directory, root);
                    foreach (var file in Directory.GetFiles(directory))
                    {
                        if (!include(Path.GetFileName(file))) continue;
                        Validate(file, root);
                        files.Add(new FileInfo(file));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { failures++; System.Diagnostics.Trace.WriteLine(ex); }
            }
            var snapshots = Path.Combine(root, "diagnostic-history");
            if (Directory.Exists(snapshots))
            {
                try
                {
                    Validate(snapshots, root);
                    foreach (var directory in Directory.GetDirectories(snapshots))
                    {
                        Validate(directory, root);
                        snapshotDirectories.Add(directory);
                        ReadFiles(directory, name => name is "ModLog.txt" or "ModLog-prev.txt" or "Player.log" or "Player-prev.log");
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { failures++; System.Diagnostics.Trace.WriteLine(ex); }
            }
            ReadFiles(Path.Combine(root, "performance", "history"), name =>
                name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && (name.StartsWith("launch-", StringComparison.Ordinal) || name.StartsWith("restart-", StringComparison.Ordinal) || name.StartsWith("restore-", StringComparison.Ordinal)));
            long total = files.Sum(file => file.Length);
            foreach (var file in files.OrderBy(file => file.LastWriteTimeUtc).ThenBy(file => file.FullName, StringComparer.Ordinal))
            {
                if (file.LastWriteTimeUtc >= cutoff && total <= maximumBytes) continue;
                try
                {
                    Validate(file.FullName, root);
                    File.Delete(file.FullName);
                    total -= file.Length;
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { failures++; System.Diagnostics.Trace.WriteLine(ex); }
            }
            foreach (var directory in snapshotDirectories)
            {
                try
                {
                    Validate(directory, root);
                    if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { failures++; System.Diagnostics.Trace.WriteLine(ex); }
            }
            return (deleted, total, failures);
        }
    }

    private static void Validate(string path, string root)
    {
        var full = Path.GetFullPath(path);
        var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Diagnostic cleanup path escaped its root.");
        for (var current = full; current != null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked diagnostic paths are excluded from cleanup.");
    }
}
