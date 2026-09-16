using System;
using System.IO;

namespace HollowKnightTAS.Core.Verification
{
    public static class EvidenceCompleteness
    {
        public static void RequireComplete(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Evidence path is required.", nameof(path));
            if (File.Exists(Path.GetFullPath(path) + ".incomplete"))
                throw new InvalidDataException("Evidence is marked incomplete: " + path);
        }
    }
}
