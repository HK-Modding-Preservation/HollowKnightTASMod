using System;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Serialization;

namespace HollowKnightTAS.Core.ReplaySave
{
    public static class ReplaySaveEntryCodec
    {
        public const int MaximumBytes =
            ReplaySaveDescriptorCodec.MaximumBytes + 1024;

        public static byte[] Serialize(ReplaySaveDescriptor descriptor)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            var descriptorBytes = ReplaySaveDescriptorCodec.Serialize(descriptor);
            var descriptorHash = Sha256Utility.ComputeHex(descriptorBytes);
            var builder = new StringBuilder(descriptorBytes.Length + 128);
            builder.Append("{\"descriptor\":");
            ReplaySaveDescriptorCodec.Append(builder, descriptor);
            builder.Append(",\"descriptorSha256\":");
            CanonicalJsonWriter.AppendString(builder, descriptorHash);
            builder.Append('}');
            var result = ReplaySaveJson.StrictUtf8.GetBytes(builder.ToString());
            if (result.Length > MaximumBytes)
            {
                throw new InvalidDataException(
                    "Replay-save entry exceeds its size limit.");
            }

            return result;
        }

        public static ReplaySaveDescriptor Deserialize(byte[] bytes)
        {
            var data = ReplaySaveJson.Deserialize<ReplaySaveEntryData>(
                bytes,
                MaximumBytes);
            if (data.Descriptor == null
                || string.IsNullOrWhiteSpace(data.DescriptorSha256))
            {
                throw new InvalidDataException(
                    "Replay-save entry is missing descriptor integrity metadata.");
            }

            byte[] decodedBytes;
            try
            {
                using (var stream = new MemoryStream())
                {
                    var serializer =
                        new System.Runtime.Serialization.Json.DataContractJsonSerializer(
                            typeof(ReplaySaveDescriptorData));
                    serializer.WriteObject(stream, data.Descriptor);
                    decodedBytes = stream.ToArray();
                }
            }
            catch (SerializationException exception)
            {
                throw new InvalidDataException(
                    "Replay-save entry descriptor cannot be decoded.",
                    exception);
            }

            var descriptor = ReplaySaveDescriptorCodec.Deserialize(decodedBytes);
            var canonicalBytes = ReplaySaveDescriptorCodec.Serialize(descriptor);
            var actualHash = Sha256Utility.ComputeHex(canonicalBytes);
            if (!string.Equals(
                    actualHash,
                    data.DescriptorSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Replay-save descriptor integrity hash does not match.");
            }

            return descriptor;
        }
    }

    [DataContract]
    internal sealed class ReplaySaveEntryData
    {
        [DataMember(Name = "descriptor")]
        public ReplaySaveDescriptorData? Descriptor { get; set; }

        [DataMember(Name = "descriptorSha256")]
        public string? DescriptorSha256 { get; set; }
    }
}
