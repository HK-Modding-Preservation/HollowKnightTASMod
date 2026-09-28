using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests;

[TestClass]
public sealed class DiagnosticLogExporterTests
{
    [TestMethod]
    public void ExportsMultipleHistoricalRunsWithoutSavesOrCredentialsAndReportsLockedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "hktas-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var local = Path.Combine(root, "local");
            var game = Path.Combine(root, "game");
            void Write(string path, string text = "diagnostic") { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
            foreach (var run in new[] { "old-run", "new-run" })
            {
                var shadow = Path.Combine(local, "save-shadows", run);
                Write(Path.Combine(shadow, "HollowKnightTAS", "sessions", "session", "events.jsonl"));
                Write(Path.Combine(shadow, "HollowKnightTAS", "sessions", "session", "full-run", "movie.hktas"));
                Write(Path.Combine(shadow, "user1.dat"), "private-save");
                Write(Path.Combine(shadow, "descriptor.json"), "private-token");
            }
            Write(Path.Combine(game, "ModLog.txt"));
            Write(Path.Combine(game, "Player-prev.log"));
            Write(Path.Combine(local, "cold-restore", "store", "operation", "failure.txt"));
            Write(Path.Combine(local, "cold-restore", "store", "operation", "claim.json"), "private-claim");
            Write(Path.Combine(local, "performance", "history", "old.json"));
            using var locked = new FileStream(Path.Combine(game, "Player.log"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            var result = DiagnosticLogExporter.Export(root, local, game);
            Assert.AreEqual(6, result.Files);
            Assert.AreEqual(1, result.Warnings);
            using var zip = ZipFile.OpenRead(result.Path);
            Assert.IsNotNull(zip.GetEntry("runs/old-run/sessions/session/events.jsonl"));
            Assert.IsNotNull(zip.GetEntry("runs/new-run/sessions/session/events.jsonl"));
            Assert.IsFalse(zip.Entries.Any(e => e.Name is "descriptor.json" or "claim.json" or "user1.dat" or "movie.hktas"));
            using var report = new StreamReader(zip.GetEntry("export-report.json")!.Open());
            StringAssert.Contains(report.ReadToEnd(), "Player.log");
            var again = DiagnosticLogExporter.Export(root, local, game);
            Assert.AreNotEqual(result.Path, again.Path);
            Assert.AreEqual(0, Directory.GetFiles(root, "*.partial").Length);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void ExportsArchivedModLogsAndIncompleteMarkersWithoutDeletingOrChangingSources()
    {
        var root = Path.Combine(Path.GetTempPath(), "hktas-diagnostic-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var game = Path.Combine(root, "game");
            var local = Path.Combine(root, "local");
            var expected = new System.Collections.Generic.Dictionary<string, string>
            {
                ["game/Old ModLogs/ModLog 09 20 2026 (10 00 00).txt"] = "old failure",
                ["game/Old ModLogs/ModLog 09 28 2026 (10 00 00).txt"] = "recent failure",
                ["game/sessions/old/events.jsonl.incomplete"] = "size limit reached",
                ["game/diagnostics/orphan.jsonl.incomplete"] = "unfinished",
                ["runs/old/sessions/session/watches.jsonl.incomplete"] = "dropped events"
            };
            var sources = new System.Collections.Generic.Dictionary<string, string>();
            void Write(string relative, string text)
            {
                var path = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
                sources.Add(path, text);
            }
            foreach (var pair in expected)
            {
                var relative = pair.Key.Replace("game/sessions/", "game/HollowKnightTAS/sessions/")
                    .Replace("game/diagnostics/", "game/HollowKnightTAS/diagnostics/")
                    .Replace("runs/old/", "local/save-shadows/old/HollowKnightTAS/");
                Write(relative, pair.Value);
            }
            Write("game/Old ModLogs/unrelated.txt", "excluded");
            Write("game/HollowKnightTAS/sessions/old/movie.hktas.incomplete", "excluded");
            var result = DiagnosticLogExporter.Export(root, local, game);
            Assert.AreEqual(expected.Count, result.Files);
            Assert.AreEqual(0, result.Warnings);
            using var zip = ZipFile.OpenRead(result.Path);
            foreach (var pair in expected)
            {
                using var reader = new StreamReader(zip.GetEntry(pair.Key)!.Open());
                Assert.AreEqual(pair.Value, reader.ReadToEnd());
            }
            foreach (var pair in sources) Assert.AreEqual(pair.Value, File.ReadAllText(pair.Key));
            Assert.IsFalse(zip.Entries.Any(e => e.Name is "unrelated.txt" or "movie.hktas.incomplete"));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void PerformanceHistorySurvivesRepeatedOperations()
    {
        var root = Path.Combine(Path.GetTempPath(), "hktas-perf-history-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var trace = new ReplayPerformanceTrace("launch", root)) trace.Mark("first");
            using (var trace = new ReplayPerformanceTrace("launch", root)) trace.Complete();
            Assert.AreEqual(2, Directory.GetFiles(Path.Combine(root, "history"), "*.json").Length);
            Assert.IsTrue(File.Exists(Path.Combine(root, "last-launch.json")));
        }
        finally { Directory.Delete(root, true); }
    }
}
