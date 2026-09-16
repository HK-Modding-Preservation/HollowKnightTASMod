using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HollowKnightTAS.Core.Diagnostics
{
    public static class BoundedJsonLinesFile
    {
        // A per-file limit, not a retention policy or a process memory limit.
        public static bool Write(string path, IEnumerable<string> lines,
            long maximumBytes = 64L * 1024 * 1024)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            if (maximumBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            path = Path.GetFullPath(path);
            var marker = path + ".incomplete";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Never replace either existing evidence or its completeness marker.
            if (File.Exists(marker)) throw new IOException("Incomplete evidence already exists: " + path);
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                using (var status = new StreamWriter(new FileStream(marker, FileMode.CreateNew,
                    FileAccess.Write, FileShare.Read), new UTF8Encoding(false)))
                    status.Write("Trace is unfinished, failed, or exceeded its byte limit.\n");
                var encoding = new UTF8Encoding(false, true);
                foreach (var line in lines)
                {
                    if (line == null || line.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                        throw new InvalidDataException("Each trace record must be a single non-null line.");
                    var count = (long)encoding.GetByteCount(line) + 1;
                    if (count > maximumBytes - output.Position) return false;
                    var bytes = encoding.GetBytes(line);
                    output.Write(bytes, 0, bytes.Length);
                    output.WriteByte((byte)'\n');
                }
                output.Flush(true);
            }
            File.Delete(marker);
            return true;
        }
    }
}
