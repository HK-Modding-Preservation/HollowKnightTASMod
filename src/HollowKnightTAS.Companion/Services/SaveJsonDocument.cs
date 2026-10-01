using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HollowKnightTAS.Companion.Services;

/// <summary>Reads only the NRBF string record used by HK; never deserializes objects.</summary>
public sealed class SaveJsonDocument
{
    private static readonly byte[] Header = { 0, 1, 0, 0, 0, 255, 255, 255, 255, 1, 0, 0, 0, 0, 0, 0, 0, 6, 1, 0, 0, 0 };
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly bool encrypted;
    public string Json { get; }

    public SaveJsonDocument(byte[] bytes)
    {
        if (bytes.Length > ProtectedSaveSession.MaximumFileBytes) throw new InvalidDataException("存档过大。");
        encrypted = bytes.AsSpan().StartsWith(Header);
        if (!encrypted && bytes.Length > 0 && bytes[0] == 0)
            throw new InvalidDataException("不支持的存档二进制格式。");
        if (encrypted)
        {
            using var stream = new MemoryStream(bytes, false);
            stream.Position = Header.Length;
            using var reader = new BinaryReader(stream, Utf8);
            int length = reader.Read7BitEncodedInt();
            if (length < 0 || length != stream.Length - stream.Position - 1)
                throw new InvalidDataException("存档字符串长度无效。");
            var cipher = Convert.FromBase64String(Utf8.GetString(reader.ReadBytes(length)));
            if (reader.ReadByte() != 11) throw new InvalidDataException("存档结束标记无效。");
            using var aes = CreateCipher();
            bytes = aes.DecryptEcb(cipher, PaddingMode.PKCS7);
        }
        var text = Utf8.GetString(bytes).TrimStart('\uFEFF');
        using var json = Parse(text);
        Json = JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonDocument Parse(string text)
    {
        if (Utf8.GetByteCount(text) > ProtectedSaveSession.MaximumFileBytes) throw new InvalidDataException("存档过大。");
        var json = JsonDocument.Parse(text);
        if (json.RootElement.ValueKind != JsonValueKind.Object)
        {
            json.Dispose();
            throw new InvalidDataException("存档 JSON 必须是对象。");
        }
        return json;
    }

    private static Aes CreateCipher()
    {
        var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes("UKu52ePUBwetZ9wNX88o54dnfKRu0T1l");
        return aes;
    }

    public byte[] Encode(string text)
    {
        using var json = Parse(text);
        var bytes = Utf8.GetBytes(json.RootElement.GetRawText());
        if (!encrypted) return bytes;
        using var aes = CreateCipher();
        var base64 = Convert.ToBase64String(aes.EncryptEcb(bytes, PaddingMode.PKCS7));
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Utf8, true);
        writer.Write(Header);
        writer.Write(base64);
        writer.Write((byte)11);
        writer.Flush();
        if (stream.Length > ProtectedSaveSession.MaximumFileBytes) throw new InvalidDataException("存档过大。");
        return stream.ToArray();
    }

    public static InitialSaveSnapshot Replace(InitialSaveSnapshot snapshot, string name, byte[] bytes)
    {
        if (!snapshot.Hashes.ContainsKey(name)) throw new InvalidDataException("存档不存在。");
        return new InitialSaveSnapshot(snapshot.CopyFiles().Select(p =>
            new System.Collections.Generic.KeyValuePair<string, byte[]>(p.Key,
                string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase) ? bytes : p.Value)));
    }

    public static string Export(InitialSaveSnapshot snapshot, string parentDirectory)
    {
        // A fresh directory plus CreateNew preserves all existing files, including real saves.
        var directory = Path.Combine(parentDirectory, "HKTAS-saves-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        snapshot.WriteToNewShadow(directory);
        return directory;
    }
}
