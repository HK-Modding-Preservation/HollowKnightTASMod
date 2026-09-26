using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HollowKnightTAS.Runtime.FullRun
{
    /// <summary>Immutable captures; all access is serialized by the Runtime command worker.</summary>
    public sealed class WorldObservationCache
    {
        private const int PageCharacters = 160000;
        private const int CacheCharacters = 32000000;
        private readonly List<Snapshot> snapshots = new List<Snapshot>();
        private readonly List<Details> details = new List<Details>();

        public string AddSnapshot(long nativeFrame, long movieFrame, string metadataJson,
            IEnumerable<Tuple<string, string, string>> objects)
        {
            var snapshot = new Snapshot { Id = Guid.NewGuid().ToString("N"), NativeFrame = nativeFrame,
                MovieFrame = movieFrame, Metadata = metadataJson, Objects = objects.ToArray() };
            snapshot.Characters = metadataJson.Length + snapshot.Objects.Sum(x => (long)x.Item3.Length);
            if (snapshot.Characters > CacheCharacters)
                throw new InvalidOperationException("World capture exceeds the 32-million-character cache budget; narrow the view.");
            snapshots.Add(snapshot);
            while (snapshots.Count > 4 || snapshots.Sum(x => x.Characters) > CacheCharacters) snapshots.RemoveAt(0);
            return snapshot.Id;
        }

        public Dictionary<string, string> ReadSnapshot(string id, int offset, int limit)
        {
            var snapshot = snapshots.FirstOrDefault(x => x.Id == id)
                ?? throw new InvalidOperationException("Snapshot expired or belongs to another session; capture a new snapshot.");
            if (offset < 0 || offset > snapshot.Objects.Length || limit < 1 || limit > 128)
                throw new ArgumentOutOfRangeException(nameof(offset));
            var root = new JObject { ["schemaVersion"] = 1, ["snapshotId"] = id,
                ["nativeFrame"] = snapshot.NativeFrame, ["movieFrame"] = snapshot.MovieFrame,
                ["metadata"] = JObject.Parse(snapshot.Metadata), ["total"] = snapshot.Objects.Length,
                ["offset"] = offset };
            var array = new JArray();
            int used = root.ToString(Formatting.None).Length;
            int index = offset;
            for (; index < snapshot.Objects.Length && index - offset < limit; index++)
            {
                var item = snapshot.Objects[index];
                var json = item.Item3;
                if (json.Length > PageCharacters / 2)
                    json = new JObject { ["id"] = item.Item1, ["kind"] = item.Item2,
                        ["detailsRequired"] = true, ["omission"] = "Overview exceeds transport budget; query getObjectDetails." }.ToString(Formatting.None);
                if (array.Count > 0 && used + json.Length > PageCharacters) break;
                array.Add(JObject.Parse(json));
                used += json.Length;
            }
            root["objects"] = array;
            root["nextOffset"] = index < snapshot.Objects.Length ? index : -1;
            var result = Frames(snapshot.NativeFrame, snapshot.MovieFrame);
            result["snapshotId"] = id;
            result["snapshotJson"] = root.ToString(Formatting.None);
            return result;
        }

        public string AddDetails(string objectId, long nativeFrame, long movieFrame, string json)
        {
            if (json.Length > CacheCharacters) throw new InvalidOperationException("Object details exceed the cache budget.");
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "").ToLowerInvariant();
            var item = new Details { Id = Guid.NewGuid().ToString("N"), ObjectId = objectId,
                NativeFrame = nativeFrame, MovieFrame = movieFrame, Json = json, Hash = hash };
            details.Add(item);
            while (details.Count > 4 || details.Sum(x => (long)x.Json.Length) > CacheCharacters) details.RemoveAt(0);
            return item.Id;
        }

        public Dictionary<string, string> ReadDetails(string id, string objectId, long? expectedFrame, int cursor, int maxCharacters)
        {
            var item = details.FirstOrDefault(x => x.Id == id)
                ?? throw new InvalidOperationException("Object details expired or belong to another session; capture new details.");
            if (item.ObjectId != objectId) throw new InvalidOperationException("detailsId belongs to a different object.");
            if (expectedFrame.HasValue && item.NativeFrame != expectedFrame.Value)
                throw new InvalidOperationException("Expected native frame differs from the captured details.");
            if (cursor < 0 || cursor > item.Json.Length || maxCharacters < 1024 || maxCharacters > 200000
                || (cursor > 0 && cursor < item.Json.Length && char.IsLowSurrogate(item.Json[cursor])))
                throw new ArgumentOutOfRangeException(nameof(cursor));
            var length = Math.Min(maxCharacters, item.Json.Length - cursor);
            if (length > 0 && cursor + length < item.Json.Length && char.IsHighSurrogate(item.Json[cursor + length - 1])) length--;
            int next = cursor + length < item.Json.Length ? cursor + length : -1;
            var result = Frames(item.NativeFrame, item.MovieFrame);
            result["detailsId"] = id; result["objectId"] = objectId;
            result["detailsJson"] = item.Json.Substring(cursor, length);
            result["cursor"] = cursor.ToString(CultureInfo.InvariantCulture);
            result["nextCursor"] = next.ToString(CultureInfo.InvariantCulture);
            result["totalCharacters"] = item.Json.Length.ToString(CultureInfo.InvariantCulture);
            result["complete"] = next == -1 ? "true" : "false";
            result["sha256"] = item.Hash;
            return result;
        }

        private static Dictionary<string, string> Frames(long nativeFrame, long movieFrame) => new Dictionary<string, string>
        { ["nativeFrame"] = nativeFrame.ToString(CultureInfo.InvariantCulture), ["movieFrame"] = movieFrame.ToString(CultureInfo.InvariantCulture) };
        private sealed class Snapshot
        {
            public string Id = "", Metadata = "";
            public long NativeFrame, MovieFrame, Characters;
            public Tuple<string, string, string>[] Objects = Array.Empty<Tuple<string, string, string>>();
        }
        private sealed class Details
        {
            public string Id = "", ObjectId = "", Json = "", Hash = "";
            public long NativeFrame, MovieFrame;
        }
    }
}
