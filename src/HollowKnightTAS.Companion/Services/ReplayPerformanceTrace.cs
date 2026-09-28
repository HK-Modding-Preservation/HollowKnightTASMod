using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace HollowKnightTAS.Companion.Services;

// Wall-clock timing only: Unity's virtual clock cannot measure replay throughput.
// Retain bounded history plus the latest report per operation; diagnostics must never prevent recovery.
public sealed class ReplayPerformanceTrace : IDisposable
{
    private readonly string operation;
    private readonly string directory;
    private readonly bool applyRetention;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly List<object> phases = new();
    private double previous;
    private bool completed;
    private bool disposed;

    public ReplayPerformanceTrace(string operation, string? directory = null)
    {
        if (operation != "launch" && operation != "restart" && operation != "restore")
            throw new ArgumentOutOfRangeException(nameof(operation));
        this.operation = operation;
        applyRetention = directory == null;
        this.directory = directory ?? Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "HollowKnightTAS", "performance");
    }

    public void Mark(string phase)
    {
        var elapsed = clock.Elapsed.TotalMilliseconds;
        phases.Add(new { phase, elapsedMs = elapsed, durationMs = elapsed - previous });
        previous = elapsed;
    }

    public void Complete() { Mark("complete"); completed = true; }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (!completed) Mark("incomplete");
        lock (DiagnosticHistoryRetention.SyncRoot)
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "last-" + operation + ".json");
            var report = JsonSerializer.Serialize(new
            {
                operation, completed, utc = DateTimeOffset.UtcNow,
                elapsedMs = clock.Elapsed.TotalMilliseconds, phases
            }, new JsonSerializerOptions { WriteIndented = true });
            var history = Path.Combine(directory, "history");
            Directory.CreateDirectory(history);
            File.WriteAllText(Path.Combine(history, operation + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N") + ".json"), report);
            File.WriteAllText(path, report);
            if (applyRetention) DiagnosticHistoryRetention.Apply(DiagnosticLogExporter.LocalRoot);
        }
        catch (Exception exception)
        {
            Trace.WriteLine("Replay performance report unavailable: " + exception.Message);
        }
    }
}
