using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace HollowKnightTAS.Companion.Services;

// Diagnostics must never turn a recoverable presentation error into an application failure.
internal sealed class StudioApplicationLog : TraceListener
{
    private readonly object sync = new();
    private StreamWriter? writer;
    private long bytes;
    private const long MaximumBytes = 2 * 1024 * 1024;

    internal StudioApplicationLog(string localRoot)
    {
        try
        {
            var directory = Path.Combine(localRoot, "application");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "studio-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ")
                + "-" + Guid.NewGuid().ToString("N") + ".log");
            writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete), new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public override bool IsThreadSafe => true;
    public override void Write(string? message) => WriteLine(message);
    public override void WriteLine(string? message)
    {
        lock (sync)
        {
            if (writer == null) return;
            var line = DateTime.UtcNow.ToString("O") + " " + message;
            var count = Encoding.UTF8.GetByteCount(line + Environment.NewLine);
            if (count > MaximumBytes - bytes) return;
            try { writer.WriteLine(line); bytes += count; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CloseWriter(); }
        }
    }

    private void CloseWriter()
    {
        try { writer?.Dispose(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        writer = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) lock (sync) CloseWriter();
        base.Dispose(disposing);
    }
}
