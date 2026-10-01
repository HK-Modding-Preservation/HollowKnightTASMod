using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests;

[TestClass]
public sealed class SaveJsonDocumentTests
{
    private const string Json = "{\"playerData\":{\"geo\":12},\"sceneData\":{},\"unknown\":9007199254740993}";

    // Same wire format as .NET Framework BinaryFormatter.Serialize(string).
    private static byte[] EncryptedSave(string json)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes("UKu52ePUBwetZ9wNX88o54dnfKRu0T1l");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)0); writer.Write(1); writer.Write(-1); writer.Write(1); writer.Write(0);
        writer.Write((byte)6); writer.Write(1);
        writer.Write(Convert.ToBase64String(aes.EncryptEcb(Encoding.UTF8.GetBytes(json), PaddingMode.PKCS7)));
        writer.Write((byte)11);
        return stream.ToArray();
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void JsonEditRoundTripsAndPreservesUnknownNumber(bool encrypted)
    {
        var original = encrypted ? EncryptedSave(Json) : Encoding.UTF8.GetBytes(Json);
        var document = new SaveJsonDocument(original);
        var bytes = document.Encode(document.Json.Replace("12", "42"));
        Assert.AreEqual(encrypted ? 0 : (byte)'{', bytes[0]);
        using var decoded = JsonDocument.Parse(new SaveJsonDocument(bytes).Json);
        Assert.AreEqual(42, decoded.RootElement.GetProperty("playerData").GetProperty("geo").GetInt32());
        Assert.AreEqual(9007199254740993L, decoded.RootElement.GetProperty("unknown").GetInt64());
        if (encrypted) CollectionAssert.AreEqual(EncryptedSave(document.Json.Replace("12", "42")), bytes);
    }

    [TestMethod] public void RejectsInvalidJsonAndNonStringBinaryRecords()
    {
        var document = new SaveJsonDocument(Encoding.UTF8.GetBytes(Json));
        Assert.Throws<JsonException>(() => document.Encode("{"));
        Assert.Throws<InvalidDataException>(() => document.Encode("[]"));
        var bytes = EncryptedSave(Json); bytes[17] = 5;
        Assert.Throws<InvalidDataException>(() => new SaveJsonDocument(bytes));
        bytes = EncryptedSave(Json); bytes[^1] = 0;
        Assert.Throws<InvalidDataException>(() => new SaveJsonDocument(bytes));
    }

    [TestMethod] public void ExportAndReplacementNeverOverwriteOriginalsOrOtherFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "save-json-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var original = EncryptedSave(Json);
            File.WriteAllBytes(Path.Combine(root, "user1.dat"), original);
            var snapshot = new InitialSaveSnapshot(new Dictionary<string, byte[]> {
                ["user1.dat"] = original, ["user1.modded.json"] = Encoding.UTF8.GetBytes("{\"mod\":true}") });
            var edited = SaveJsonDocument.Replace(snapshot, "user1.dat", EncryptedSave(Json.Replace("12", "42")));
            var export = SaveJsonDocument.Export(edited, root);
            Assert.AreNotEqual(snapshot.Id, edited.Id);
            Assert.AreEqual(snapshot.Hashes["user1.modded.json"], edited.Hashes["user1.modded.json"]);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(Path.Combine(root, "user1.dat")));
            Assert.AreEqual(edited.Id, new InitialSaveSnapshot(new Dictionary<string, byte[]> {
                ["user1.dat"] = File.ReadAllBytes(Path.Combine(export, "user1.dat")),
                ["user1.modded.json"] = File.ReadAllBytes(Path.Combine(export, "user1.modded.json")) }).Id);
            Assert.AreNotEqual(export, SaveJsonDocument.Export(edited, root));
        }
        finally { Directory.Delete(root, true); }
    }
}
