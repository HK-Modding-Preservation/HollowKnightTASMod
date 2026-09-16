using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using HollowKnightTAS.Core.Cryptography;

namespace HollowKnightTAS.Core.Diagnostics
{
    // Only cooperating JSONL writers are capped. Existing evidence is never pruned.
    public sealed class DirectoryLogBudget : IDisposable
    {
        public const long DefaultMaximumBytes = 1024L * 1024 * 1024;
        private readonly string root;
        private readonly string mutexName;
        private string? reservation;
        private DirectoryLogBudget(string root)
        {
            this.root = root;
            mutexName = "HKTAS-LogBudget-" + Sha256Utility.ComputeHex(Encoding.UTF8.GetBytes(
                Path.DirectorySeparatorChar == '\\' ? root.ToUpperInvariant() : root));
        }
        public long GrantedBytes { get; private set; }

        public static DirectoryLogBudget Reserve(string directory, string outputPath, long requested, long maximum)
        {
            if (requested < 0 || maximum <= 0) throw new ArgumentOutOfRangeException(nameof(maximum));
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var output = Path.GetFullPath(outputPath);
            if (string.Equals(root, Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A drive root cannot be a log budget directory.");
            if (!output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Log output must be inside its budget root.");
            Directory.CreateDirectory(root);
            var budget = new DirectoryLogBudget(root);
            budget.WithLock(() =>
            {
                var files = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                var pending = new Stack<string>();
                pending.Push(root);
                while (pending.Count > 0)
                {
                    var current = pending.Pop();
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Budget directories cannot contain reparse points.");
                    foreach (var file in Directory.EnumerateFiles(current, "*.jsonl"))
                    {
                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                            throw new IOException("Budget logs cannot be reparse points.");
                        files[file] = new FileInfo(file).Length;
                    }
                    foreach (var child in Directory.EnumerateDirectories(current)) pending.Push(child);
                }
                var reservations = Path.Combine(root, ".log-reservations");
                Directory.CreateDirectory(reservations);
                foreach (var file in Directory.EnumerateFiles(reservations, "*.reserved"))
                {
                    var lines = File.ReadAllLines(file);
                    if (lines.Length != 2 || !long.TryParse(lines[0], out var amount) || amount < 0)
                        throw new InvalidDataException("Malformed log reservation; refusing additional allocation.");
                    var path = Encoding.UTF8.GetString(Convert.FromBase64String(lines[1]));
                    files.TryGetValue(path, out var actual);
                    files[path] = Math.Max(actual, amount);
                }
                var used = files.Values.Aggregate(0L, (sum, value) => checked(sum + value));
                budget.GrantedBytes = Math.Min(requested, Math.Max(0L, maximum - used));
                if (budget.GrantedBytes == 0) return;
                var marker = Path.Combine(reservations, Guid.NewGuid().ToString("N") + ".reserved");
                var bytes = Encoding.UTF8.GetBytes(budget.GrantedBytes.ToString(CultureInfo.InvariantCulture) + "\n"
                    + Convert.ToBase64String(Encoding.UTF8.GetBytes(output)) + "\n");
                using (var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                budget.reservation = marker;
            });
            return budget;
        }

        private void WithLock(Action action)
        {
            using (var mutex = new Mutex(false, mutexName))
            {
                var acquired = false;
                try
                {
                    try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new IOException("Log budget is busy.");
                    action();
                }
                finally { if (acquired) mutex.ReleaseMutex(); }
            }
        }
        public void Dispose()
        {
            WithLock(() =>
            {
                if (reservation == null) return;
                File.Delete(reservation); // Only this writer's allocation marker, never log evidence.
                reservation = null;
            });
        }
    }
}
