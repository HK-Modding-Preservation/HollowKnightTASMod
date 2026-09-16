using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using HollowKnightTAS.Core.Serialization;

namespace HollowKnightTAS.Core.ReplaySave
{
    public sealed class ReplaySaveCatalogEntry
    {
        public ReplaySaveCatalogEntry(
            ReplaySaveDescriptor descriptor,
            ReplaySaveStatus status,
            string detail)
        {
            Descriptor = descriptor
                         ?? throw new ArgumentNullException(nameof(descriptor));
            if (!Enum.IsDefined(typeof(ReplaySaveStatus), status))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            Status = status;
            Detail = detail ?? string.Empty;
        }

        public ReplaySaveDescriptor Descriptor { get; }
        public ReplaySaveStatus Status { get; }
        public string Detail { get; }
    }

    public sealed class ReplaySaveCatalog
    {
        public const int CurrentSchemaVersion = 1;
        public const int MaximumEntries = 10000;
        public const int MaximumCanonicalBytes = 16 * 1024 * 1024;

        private readonly Dictionary<string, ReplaySaveCatalogEntry> entries =
            new Dictionary<string, ReplaySaveCatalogEntry>(StringComparer.Ordinal);

        public ReplaySaveCatalog(long revision = 0)
        {
            if (revision < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(revision));
            }

            Revision = revision;
        }

        public long Revision { get; private set; }

        public IReadOnlyList<ReplaySaveCatalogEntry> Entries =>
            new ReadOnlyCollection<ReplaySaveCatalogEntry>(
                entries.Values
                    .OrderBy(value => value.Descriptor.EffectiveMovieTick)
                    .ThenBy(value => value.Descriptor.CreatedAtUtc)
                    .ThenBy(
                        value => value.Descriptor.ReplaySaveId,
                        StringComparer.Ordinal)
                    .ToList());

        public void Upsert(ReplaySaveCatalogEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (!entries.ContainsKey(entry.Descriptor.ReplaySaveId)
                && entries.Count >= MaximumEntries)
            {
                throw new InvalidOperationException(
                    "Replay-save catalog entry limit was reached.");
            }

            entries[entry.Descriptor.ReplaySaveId] = entry;
            Revision = checked(Revision + 1);
        }

        public bool Remove(string replaySaveId)
        {
            if (entries.Remove(replaySaveId))
            {
                Revision = checked(Revision + 1);
                return true;
            }

            return false;
        }

        public bool TryGet(
            string replaySaveId,
            out ReplaySaveCatalogEntry? entry)
        {
            if (entries.TryGetValue(replaySaveId, out var found))
            {
                entry = found;
                return true;
            }

            entry = null;
            return false;
        }

        internal void RestoreRevision(long value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Revision = value;
        }
    }

    public static class ReplaySaveCatalogCodec
    {
        public static byte[] Serialize(ReplaySaveCatalog value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var builder = new StringBuilder(
                Math.Max(1024, value.Entries.Count * 2048));
            builder.Append("{\"entries\":[");
            for (var index = 0; index < value.Entries.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var entry = value.Entries[index];
                builder.Append("{\"descriptor\":");
                ReplaySaveDescriptorCodec.Append(builder, entry.Descriptor);
                builder.Append(",\"detail\":");
                CanonicalJsonWriter.AppendString(builder, entry.Detail);
                builder.Append(",\"status\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    entry.Status.ToString());
                builder.Append('}');
            }

            builder.Append("],\"revision\":");
            builder.Append(
                value.Revision.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(",\"schemaVersion\":");
            builder.Append(
                ReplaySaveCatalog.CurrentSchemaVersion.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            builder.Append('}');
            var bytes = ReplaySaveJson.StrictUtf8.GetBytes(builder.ToString());
            if (bytes.Length > ReplaySaveCatalog.MaximumCanonicalBytes)
            {
                throw new InvalidDataException(
                    "Replay-save catalog exceeds its size limit.");
            }

            return bytes;
        }

        public static ReplaySaveCatalog Deserialize(byte[] bytes)
        {
            var data = ReplaySaveJson.Deserialize<ReplaySaveCatalogData>(
                bytes,
                ReplaySaveCatalog.MaximumCanonicalBytes);
            if (data.SchemaVersion != ReplaySaveCatalog.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    "Unsupported replay-save catalog schema: "
                    + data.SchemaVersion
                    + ".");
            }

            if (data.Revision < 0
                || data.Entries == null
                || data.Entries.Length > ReplaySaveCatalog.MaximumEntries)
            {
                throw new InvalidDataException(
                    "Replay-save catalog metadata is invalid.");
            }

            var catalog = new ReplaySaveCatalog();
            foreach (var item in data.Entries)
            {
                if (item?.Descriptor == null
                    || !Enum.TryParse(
                        item.Status,
                        ignoreCase: false,
                        out ReplaySaveStatus status)
                    || !Enum.IsDefined(typeof(ReplaySaveStatus), status))
                {
                    throw new InvalidDataException(
                        "Replay-save catalog entry is invalid.");
                }

                var descriptor = ReplaySaveDescriptorCodec.Deserialize(
                    SerializeDescriptorData(item.Descriptor));
                if (catalog.TryGet(descriptor.ReplaySaveId, out _))
                {
                    throw new InvalidDataException(
                        "Replay-save catalog contains a duplicate ID.");
                }

                catalog.Upsert(
                    new ReplaySaveCatalogEntry(
                        descriptor,
                        status,
                        item.Detail ?? string.Empty));
            }

            catalog.RestoreRevision(data.Revision);
            return catalog;
        }

        private static byte[] SerializeDescriptorData(
            ReplaySaveDescriptorData data)
        {
            try
            {
                using (var stream = new MemoryStream())
                {
                    var serializer =
                        new System.Runtime.Serialization.Json.DataContractJsonSerializer(
                            typeof(ReplaySaveDescriptorData));
                    serializer.WriteObject(stream, data);
                    return stream.ToArray();
                }
            }
            catch (SerializationException exception)
            {
                throw new InvalidDataException(
                    "Replay-save descriptor data could not be decoded.",
                    exception);
            }
        }
    }

    [DataContract]
    internal sealed class ReplaySaveCatalogData
    {
        [DataMember(Name = "schemaVersion")]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "revision")]
        public long Revision { get; set; }

        [DataMember(Name = "entries")]
        public ReplaySaveCatalogEntryData[]? Entries { get; set; }
    }

    [DataContract]
    internal sealed class ReplaySaveCatalogEntryData
    {
        [DataMember(Name = "descriptor")]
        public ReplaySaveDescriptorData? Descriptor { get; set; }

        [DataMember(Name = "status")]
        public string? Status { get; set; }

        [DataMember(Name = "detail")]
        public string? Detail { get; set; }
    }
}
