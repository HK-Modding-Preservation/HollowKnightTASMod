using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HollowKnightTAS.Companion.Services
{
    // References only: this file never contains or deletes game/save payloads.
    public sealed class StudioQuickSlot
    {
        public string SaveId { get; set; } = "";
        public long Tick { get; set; } = -1;
        public string PendingLabel { get; set; } = "";
    }

    public sealed class StudioQuickSlots
    {
        private readonly string? path;
        public StudioQuickSlot[] Slots { get; private set; } = Enumerable.Range(0, 10).Select(_ => new StudioQuickSlot()).ToArray();
        public StudioQuickSlots(string? path = null)
        {
            this.path = path;
            if (path == null || !File.Exists(path)) return;
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("快捷槽配置过大；未覆盖原文件。");
            var slots = JsonSerializer.Deserialize<StudioQuickSlot[]>(File.ReadAllText(path));
            if (slots == null || slots.Length != 10 || slots.Any(s => s == null || s.SaveId == null || s.PendingLabel == null))
                throw new InvalidDataException("快捷槽配置损坏；未覆盖原文件。");
            Slots = slots;
        }

        public void Set(int index, string saveId, long tick, string pendingLabel)
        {
            if (index < 0 || index >= 10) throw new ArgumentOutOfRangeException(nameof(index));
            var next = (StudioQuickSlot[])Slots.Clone();
            next[index] = new StudioQuickSlot { SaveId = saveId, Tick = tick, PendingLabel = pendingLabel };
            if (path == null) { Slots = next; return; }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(next));
                File.Move(temp, path, true);
                Slots = next;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        public void Reconcile(string catalogJson)
        {
            using var document = JsonDocument.Parse(catalogJson);
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.GetProperty("status").GetString() != "Ready") continue;
                var label = entry.GetProperty("label").GetString();
                for (var i = 0; i < Slots.Length; i++)
                    if (Slots[i].PendingLabel.Length > 0 && Slots[i].PendingLabel == label)
                        Set(i, entry.GetProperty("id").GetString()!, entry.GetProperty("effectiveMovieTick").GetInt64(), "");
            }
        }
    }
}
