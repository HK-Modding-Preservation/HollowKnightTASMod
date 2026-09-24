using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Core.ReplaySave
{
    public sealed class ProtectedSaveDescriptor
    {
        private readonly IReadOnlyDictionary<string, string> originalFileSha256;
        private readonly IReadOnlyList<int> absentSlots;

        public ProtectedSaveDescriptor(string runId, string originalRoot, string shadowRoot,
            IReadOnlyDictionary<string, string> originalFileSha256, string guardToken)
        {
            if (!IsSafeRunId(runId)) throw new ArgumentException("Invalid protected run ID.", nameof(runId));
            if (string.IsNullOrWhiteSpace(originalRoot)) throw new ArgumentException("Original save root is required.", nameof(originalRoot));
            if (string.IsNullOrWhiteSpace(shadowRoot)) throw new ArgumentException("Shadow save root is required.", nameof(shadowRoot));
            if (!MovieProtocolV1.IsLowerSha256(guardToken))
                throw new ArgumentException("Guard token must be 32 random bytes in lowercase hex.", nameof(guardToken));
            if (originalFileSha256 == null) throw new ArgumentNullException(nameof(originalFileSha256));
            var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in originalFileSha256)
            {
                if (!IsSlotFileName(pair.Key) || !MovieProtocolV1.IsLowerSha256(pair.Value))
                    throw new ArgumentException("Invalid original save file identity.", nameof(originalFileSha256));
                if (copy.ContainsKey(pair.Key))
                    throw new ArgumentException("Duplicate original save file name.", nameof(originalFileSha256));
                copy.Add(pair.Key, pair.Value);
            }
            RunId = runId;
            OriginalRoot = originalRoot;
            ShadowRoot = shadowRoot;
            GuardToken = guardToken;
            this.originalFileSha256 = new ReadOnlyDictionary<string, string>(copy);
            var missing = new List<int>();
            for (var slot = 1; slot <= 4; slot++)
            {
                var prefix = "user" + slot + ".";
                var exists = false;
                foreach (var name in copy.Keys)
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                if (!exists) missing.Add(slot);
            }
            absentSlots = Array.AsReadOnly(missing.ToArray());
        }

        public string RunId { get; }
        public string OriginalRoot { get; }
        public string ShadowRoot { get; }
        public IReadOnlyDictionary<string, string> OriginalFileSha256 => originalFileSha256;
        public string GuardToken { get; }
        public IReadOnlyList<int> AbsentSlots => absentSlots;

        public static bool IsSlotFileName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 7 || name.Length > 160
                || !name.StartsWith("user", StringComparison.OrdinalIgnoreCase)
                || name[4] < '1' || name[4] > '4' || name[5] != '.'
                || name[name.Length - 1] == '.' || name[name.Length - 1] == ' ')
                return false;
            for (var index = 6; index < name.Length; index++)
            {
                var character = name[index];
                if (character < 0x20 || character == '/' || character == '\\' || character == ':') return false;
            }
            return true;
        }

        public static bool IsSafeRunId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64) return false;
            foreach (var character in value)
                if (!(character >= 'a' && character <= 'z')
                    && !(character >= 'A' && character <= 'Z')
                    && !(character >= '0' && character <= '9')
                    && character != '-' && character != '_') return false;
            return true;
        }
    }
}
