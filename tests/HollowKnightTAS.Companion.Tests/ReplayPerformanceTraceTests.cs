using System;
using System.IO;
using System.Text.Json;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests;

[TestClass]
public sealed class ReplayPerformanceTraceTests
{
    [TestMethod]
    public void FailureRetainsCompletedPhasesWithoutClaimingSuccess()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hktas-perf-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var trace = new ReplayPerformanceTrace("restore", directory)) trace.Mark("source-exited");
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "last-restore.json")));
            Assert.IsFalse(report.RootElement.GetProperty("completed").GetBoolean());
            var phases = report.RootElement.GetProperty("phases");
            Assert.AreEqual("source-exited", phases[0].GetProperty("phase").GetString());
            Assert.AreEqual("incomplete", phases[1].GetProperty("phase").GetString());
            Assert.IsTrue(phases[1].GetProperty("durationMs").GetDouble() >= 0);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void UnwritableReportDoesNotBreakRestoreCleanup()
    {
        var file = Path.GetTempFileName();
        try
        {
            using var trace = new ReplayPerformanceTrace("restart", file);
            trace.Complete();
        }
        finally { File.Delete(file); }
    }
}
