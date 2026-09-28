using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace HollowKnightTAS.Companion.Services;

// Wall-clock timing only: Unity's virtual clock cannot measure replay throughput.
// Keep one report per operation type; diagnostics must never prevent recovery.
public sealed class ReplayPerformanceTrace : IDisposable
{
    private readonly string operation;
    private readonly string directory;
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
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "last-" + operation + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                operation, completed, utc = DateTimeOffset.UtcNow,
                elapsedMs = clock.Elapsed.TotalMilliseconds, phases
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            Trace.WriteLine("Replay performance report unavailable: " + exception.Message);
        }
    }
}
