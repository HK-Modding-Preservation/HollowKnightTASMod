using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace HollowKnightTAS.Runtime.ReplaySave
{
    public interface ISavePathResolver
    {
        string GetSlotPath(int slot, string suffix);
        string GetTasDataPath(params string[] segments);
    }

    public sealed class DesktopSavePathResolver : ISavePathResolver
    {
        private readonly string root;

        public DesktopSavePathResolver(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("Save root is required.", nameof(root));
            this.root = Path.GetFullPath(root);
        }

        public string GetSlotPath(int slot, string suffix)
        {
            if (slot < 1 || slot > 4)
                throw new ArgumentOutOfRangeException(nameof(slot));
            if (string.IsNullOrEmpty(suffix) || suffix[0] != '.'
                || suffix == "." || suffix.Contains(".."))
                throw new ArgumentException("Invalid slot suffix.", nameof(suffix));
            for (var index = 1; index < suffix.Length; index++)
            {
                var character = suffix[index];
                if (!(character >= 'a' && character <= 'z')
                    && !(character >= 'A' && character <= 'Z')
                    && !(character >= '0' && character <= '9')
                    && character != '.' && character != '-')
                    throw new ArgumentException("Invalid slot suffix.", nameof(suffix));
            }
            return Path.Combine(root, "user" + slot.ToString(CultureInfo.InvariantCulture) + suffix);
        }

        public string GetTasDataPath(params string[] segments)
        {
            var path = Path.Combine(root, "HollowKnightTAS");
            if (segments == null) throw new ArgumentNullException(nameof(segments));
            foreach (var segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment) || segment == "."
                    || segment == ".." || Path.GetFileName(segment) != segment
                    || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new ArgumentException("Invalid TAS data path segment.", nameof(segments));
                path = Path.Combine(path, segment);
            }
            return path;
        }
    }

    public static class SavePathResolver
    {
        private static ISavePathResolver? protectedResolver;

        public static bool ProtectionRequested =>
            string.Equals(Environment.GetEnvironmentVariable("HKTAS_FULL_RUN_SAVE_GUARD"),
                "1", StringComparison.Ordinal);

        public static ISavePathResolver Current
        {
            get
            {
                var resolver = protectedResolver;
                if (resolver != null) return resolver;
                if (ProtectionRequested)
                    throw new InvalidOperationException(
                        "Protected save resolver is not installed; original save paths are unavailable.");
                return new DesktopSavePathResolver(Application.persistentDataPath);
            }
        }

        internal static void SetProtected(ISavePathResolver resolver)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            if (protectedResolver != null)
                throw new InvalidOperationException("Protected save resolver is already installed.");
            protectedResolver = resolver;
        }

        internal static void ClearProtected(ISavePathResolver resolver)
        {
            if (ReferenceEquals(protectedResolver, resolver)) protectedResolver = null;
        }
    }
}
