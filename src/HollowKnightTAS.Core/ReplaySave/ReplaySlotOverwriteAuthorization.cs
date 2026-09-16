using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HollowKnightTAS.Core.Cryptography;

namespace HollowKnightTAS.Core.ReplaySave
{
    public sealed class ReplaySlotFileIdentity
    {
        public ReplaySlotFileIdentity(int slot, string saveSha256, string moddedSha256)
        {
            if (slot < 1 || slot > 4) throw new ArgumentOutOfRangeException(nameof(slot));
            Slot = slot;
            SaveSha256 = OptionalHash(saveSha256);
            ModdedSha256 = OptionalHash(moddedSha256);
        }
        public int Slot { get; }
        // Empty means absent, not an existing zero-byte file.
        public string SaveSha256 { get; }
        public string ModdedSha256 { get; }
        private static string OptionalHash(string value) => value == string.Empty ? value
            : ReplaySaveDescriptor.RequireSha256(value, nameof(value));
        public bool Matches(byte[]? save, byte[]? modded) =>
            SaveSha256 == (save == null ? string.Empty : Sha256Utility.ComputeHex(save))
            && ModdedSha256 == (modded == null ? string.Empty : Sha256Utility.ComputeHex(modded));
    }

    // Durable scope of explicit source-side consent. Not a blanket slot grant:
    // the execution identity binds every target file and operation, while Slots
    // binds the pre-existing user files that may be temporarily replaced.
    public sealed class ReplaySlotOverwriteAuthorization
    {
        public const int MaximumTextLength = 2048;
        public ReplaySlotOverwriteAuthorization(string baselineSha256, string movieSha256,
            string lifecycleSha256, long targetTick, IEnumerable<ReplaySlotFileIdentity> slots)
        {
            BaselineSha256 = ReplaySaveDescriptor.RequireSha256(baselineSha256, nameof(baselineSha256));
            MovieSha256 = ReplaySaveDescriptor.RequireSha256(movieSha256, nameof(movieSha256));
            LifecycleSha256 = ReplaySaveDescriptor.RequireSha256(lifecycleSha256, nameof(lifecycleSha256));
            if (targetTick < 0) throw new ArgumentOutOfRangeException(nameof(targetTick));
            TargetTick = targetTick;
            var copied = (slots ?? throw new ArgumentNullException(nameof(slots))).Take(5).ToArray();
            if (copied.Length == 0 || copied.Length > 4 || copied.Any(x => x == null)
                || copied.Select(x => x.Slot).Distinct().Count() != copied.Length)
                throw new ArgumentException("Consent requires one to four distinct slots.", nameof(slots));
            Slots = Array.AsReadOnly(copied.OrderBy(x => x.Slot).ToArray());
        }
        public string BaselineSha256 { get; }
        public string MovieSha256 { get; }
        public string LifecycleSha256 { get; }
        public long TargetTick { get; }
        public IReadOnlyList<ReplaySlotFileIdentity> Slots { get; }
        public bool MatchesExecution(string baseline, string movie, string lifecycle, long targetTick) =>
            baseline == BaselineSha256 && movie == MovieSha256 && lifecycle == LifecycleSha256 && targetTick == TargetTick;
        public bool Allows(int slot, byte[]? currentSave, byte[]? currentModded) =>
            Slots.Any(x => x.Slot == slot && x.Matches(currentSave, currentModded));
        public string Serialize() => string.Join("\n", new[] { "HKSA1", BaselineSha256, MovieSha256,
            LifecycleSha256, TargetTick.ToString(CultureInfo.InvariantCulture) }
            .Concat(Slots.Select(x => x.Slot.ToString(CultureInfo.InvariantCulture) + "|" + x.SaveSha256 + "|" + x.ModdedSha256)));
        public static ReplaySlotOverwriteAuthorization Deserialize(string text)
        {
            if (text == null || text.Length > MaximumTextLength) throw new InvalidDataException("Invalid slot consent size.");
            try
            {
                var lines = text.Split('\n');
                if (lines.Length < 6 || lines.Length > 9 || lines[0] != "HKSA1") throw new InvalidDataException("Invalid slot consent format.");
                var slots = lines.Skip(5).Select(line =>
                {
                    var fields = line.Split('|');
                    if (fields.Length != 3) throw new InvalidDataException("Invalid slot consent entry.");
                    return new ReplaySlotFileIdentity(int.Parse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture), fields[1], fields[2]);
                });
                var result = new ReplaySlotOverwriteAuthorization(lines[1], lines[2], lines[3],
                    long.Parse(lines[4], NumberStyles.None, CultureInfo.InvariantCulture), slots);
                if (result.Serialize() != text) throw new InvalidDataException("Noncanonical slot consent.");
                return result;
            }
            catch (Exception error) when (error is ArgumentException || error is FormatException || error is OverflowException)
            { throw new InvalidDataException("Invalid slot consent.", error); }
        }
    }
}
