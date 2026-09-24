using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace HollowKnightTAS.Core.ReplaySave
{
    public static class ProtectedSaveDescriptorCodec
    {
        public const int MaximumBytes = 32768;
        private const string Format = "hktas-protected-save-v1";

        public static byte[] Serialize(ProtectedSaveDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            var files = new List<FileEntry>();
            foreach (var pair in descriptor.OriginalFileSha256)
                files.Add(new FileEntry { Name = pair.Key, Sha256 = pair.Value });
            files.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
            var record = new DescriptorRecord
            {
                Format = Format,
                RunId = descriptor.RunId,
                OriginalRoot = descriptor.OriginalRoot,
                ShadowRoot = descriptor.ShadowRoot,
                GuardToken = descriptor.GuardToken,
                Files = files.ToArray()
            };
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(DescriptorRecord)).WriteObject(stream, record);
                if (stream.Length > MaximumBytes) throw new InvalidDataException("Protected save descriptor is too large.");
                return stream.ToArray();
            }
        }

        public static ProtectedSaveDescriptor Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaximumBytes)
                throw new InvalidDataException("Protected save descriptor size is invalid.");
            DescriptorRecord record;
            using (var stream = new MemoryStream(bytes, false))
                record = (DescriptorRecord)(new DataContractJsonSerializer(typeof(DescriptorRecord))
                    .ReadObject(stream) ?? throw new InvalidDataException("Protected save descriptor is empty."));
            if (record.Format != Format || record.Files == null)
                throw new InvalidDataException("Protected save descriptor format is invalid.");
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in record.Files)
            {
                if (file == null || file.Name == null || file.Sha256 == null
                    || files.ContainsKey(file.Name))
                    throw new InvalidDataException("Protected save descriptor file list is invalid.");
                files.Add(file.Name, file.Sha256);
            }
            return new ProtectedSaveDescriptor(record.RunId ?? string.Empty,
                record.OriginalRoot ?? string.Empty, record.ShadowRoot ?? string.Empty,
                files, record.GuardToken ?? string.Empty);
        }

        [DataContract]
        private sealed class DescriptorRecord
        {
            [DataMember(Name = "format", Order = 0)] public string? Format { get; set; }
            [DataMember(Name = "runId", Order = 1)] public string? RunId { get; set; }
            [DataMember(Name = "originalRoot", Order = 2)] public string? OriginalRoot { get; set; }
            [DataMember(Name = "shadowRoot", Order = 3)] public string? ShadowRoot { get; set; }
            [DataMember(Name = "guardToken", Order = 4)] public string? GuardToken { get; set; }
            [DataMember(Name = "files", Order = 5)] public FileEntry[]? Files { get; set; }
        }

        [DataContract]
        private sealed class FileEntry
        {
            [DataMember(Name = "name", Order = 0)] public string? Name { get; set; }
            [DataMember(Name = "sha256", Order = 1)] public string? Sha256 { get; set; }
        }
    }
}
