using System;
using System.IO;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests;

[TestClass]
public sealed class DiagnosticHistoryRetentionTests
{
    [TestMethod]
    public void DeletesExpiredThenOldestAcrossBothHistoriesAndProtectsOtherData()
    {
        var root = Path.Combine(Path.GetTempPath(), "hktas-retention-" + Guid.NewGuid().ToString("N"));
        try
        {
            var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
            string Write(string relative, int age)
            {
                var path = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, new byte[10]);
                File.SetLastWriteTimeUtc(path, now.AddDays(-age));
                return path;
            }
            var expired = Write("diagnostic-history/expired/ModLog.txt", 8);
            var old = Write("performance/history/launch-old.json", 6);
            var recent = Write("diagnostic-history/recent/Player.log", 1);
            var newest = Write("performance/history/restore-new.json", 0);
            var protectedFiles = new[]
            {
                Write("save-shadows/run/user1.dat", 20),
                Write("save-shadows/run/HollowKnightTAS/sessions/events.jsonl", 20),
                Write("performance/last-launch.json", 20),
                Write("diagnostic-history/expired/unrelated.json", 20),
                Write("performance/history/unrelated.json", 20)
            };
            var result = DiagnosticHistoryRetention.Apply(root, now, 20);
            Assert.AreEqual(2, result.Deleted);
            Assert.AreEqual(20L, result.RemainingBytes);
            Assert.AreEqual(0, result.Failures);
            Assert.IsFalse(File.Exists(expired)); Assert.IsFalse(File.Exists(old));
            Assert.IsTrue(File.Exists(recent)); Assert.IsTrue(File.Exists(newest));
            foreach (var path in protectedFiles) Assert.IsTrue(File.Exists(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void LockedHistoryDoesNotBreakCleanupAndCanBeRetried()
    {
        var root = Path.Combine(Path.GetTempPath(), "hktas-retention-locked-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "diagnostic-history", "old");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Player.log");
            File.WriteAllBytes(path, new byte[10]);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-8));
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var result = DiagnosticHistoryRetention.Apply(root);
                Assert.AreEqual(0, result.Deleted);
                Assert.AreEqual(1, result.Failures);
                Assert.IsTrue(File.Exists(path));
            }
            Assert.AreEqual(1, DiagnosticHistoryRetention.Apply(root).Deleted);
            Assert.IsFalse(Directory.Exists(directory));
        }
        finally { Directory.Delete(root, true); }
    }
}
