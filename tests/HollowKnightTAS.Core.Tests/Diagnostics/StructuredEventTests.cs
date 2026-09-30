using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HollowKnightTAS.Core.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Diagnostics
{
    [TestClass]
    public sealed class StructuredEventTests
    {
        [TestMethod]
        public void JsonLinesSink_FlushesIdleTailWithoutExplicitFlushOrDispose()
        {
            var directory = Path.Combine(Path.GetTempPath(), "hktas-live-log-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "events.jsonl");
            try
            {
                using var sink = new JsonLinesEventSink(path, 8, TimeSpan.FromSeconds(2));
                for (var sequence = 1; sequence <= 2; sequence++)
                {
                    sink.Emit(CreateEvent(sequence));
                    Assert.IsTrue(System.Threading.SpinWait.SpinUntil(() =>
                    {
                        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var reader = new StreamReader(stream);
                        var lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length != sequence) return false;
                        try
                        {
                            using var json = JsonDocument.Parse(lines[sequence - 1]);
                            return json.RootElement.GetProperty("sequence").GetInt64() == sequence;
                        }
                        catch (JsonException) { return false; }
                    }, TimeSpan.FromSeconds(5)), "The running writer left its partial buffer unflushed.");
                }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [TestMethod]
        public void Serialize_SortsFieldsAndEscapesControlCharacters()
        {
            var value = new StructuredEvent(
                1,
                "session-测试",
                1,
                "test-event",
                new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero),
                new Dictionary<string, string>
                {
                    ["z"] = "line\nbreak",
                    ["a"] = "quote\"slash\\"
                });

            var json = StructuredEventJson.Serialize(value);
            Assert.IsTrue(
                json.IndexOf("\"a\"", StringComparison.Ordinal)
                < json.IndexOf("\"z\"", StringComparison.Ordinal));

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual(1L, document.RootElement.GetProperty("sequence").GetInt64());
            Assert.AreEqual(
                "line\nbreak",
                document.RootElement.GetProperty("fields").GetProperty("z").GetString());
        }

        [TestMethod]
        public void JsonLinesSink_WritesParseableStrictlyIncreasingLines()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "HollowKnightTAS.Core.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "events.jsonl");

            try
            {
                using (var sink = new JsonLinesEventSink(
                           path,
                           256,
                           TimeSpan.FromSeconds(2)))
                {
                    for (var sequence = 1; sequence <= 100; sequence++)
                    {
                        sink.Emit(
                            new StructuredEvent(
                                1,
                                "session",
                                sequence,
                                "event",
                                DateTimeOffset.UtcNow,
                                new Dictionary<string, string>
                                {
                                    ["value"] = sequence.ToString()
                                }));
                    }

                    sink.Flush(TimeSpan.FromSeconds(2));
                    Assert.AreEqual(0L, sink.DroppedCount);
                }

                var bytes = File.ReadAllBytes(path);
                Assert.IsFalse(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
                var lines = File.ReadAllLines(path, new UTF8Encoding(false, true));
                Assert.AreEqual(100, lines.Length);

                long previous = 0;
                foreach (var line in lines)
                {
                    using var parsed = JsonDocument.Parse(line);
                    var current = parsed.RootElement.GetProperty("sequence").GetInt64();
                    Assert.IsTrue(current > previous);
                    previous = current;
                }
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [TestMethod]
        public void JsonLinesSink_RejectsNonIncreasingSequence()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "HollowKnightTAS.Core.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "events.jsonl");

            try
            {
                using var sink = new JsonLinesEventSink(
                    path,
                    8,
                    TimeSpan.FromSeconds(2));
                sink.Emit(CreateEvent(2));
                sink.Emit(CreateEvent(1));
                Assert.ThrowsExactly<IOException>(() => sink.Flush(TimeSpan.FromSeconds(2)));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [TestMethod]
        public void JsonLinesSink_SizeLimitPreservesWholeLinesAndFlushesAfterTruncation()
        {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "events.jsonl");
            var first = new StructuredEvent(1, "session", 1, "event",
                DateTimeOffset.UtcNow, new Dictionary<string, string> { ["text"] = "中文" });
            var limit = Encoding.UTF8.GetByteCount(StructuredEventJson.Serialize(first)) + 1;
            try
            {
                using (var sink = new JsonLinesEventSink(path, 8, TimeSpan.FromSeconds(2), limit))
                {
                    sink.Emit(first);
                    sink.Flush(TimeSpan.FromSeconds(2));
                    Assert.IsFalse(sink.SizeLimitReached);
                    sink.Emit(CreateEvent(2));
                    sink.Flush(TimeSpan.FromSeconds(2));
                    Assert.IsTrue(sink.SizeLimitReached);
                    sink.Emit(CreateEvent(3));
                    sink.Flush(TimeSpan.FromSeconds(2));
                    Assert.AreEqual(2L, sink.DroppedCount);
                }
                Assert.AreEqual((long)limit, new FileInfo(path).Length);
                Assert.AreEqual(1, File.ReadAllLines(path).Length);
                Assert.IsTrue(File.Exists(path + ".incomplete"));
                using var parsed = JsonDocument.Parse(File.ReadAllText(path));
                Assert.AreEqual(1L, parsed.RootElement.GetProperty("sequence").GetInt64());
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void JsonLinesSink_RefusesToOverwriteExistingEvidence()
        {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "events.jsonl");
            try
            {
                File.WriteAllText(path, "retained evidence");
                Assert.ThrowsExactly<IOException>(() =>
                    new JsonLinesEventSink(path, 8, TimeSpan.FromSeconds(2)));
                Assert.AreEqual("retained evidence", File.ReadAllText(path));
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestMethod]
        public void JsonLinesSink_DroppedEventsMarkIncompleteOnFlush()
        {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "events.jsonl");
            try
            {
                using var sink = new JsonLinesEventSink(path, 8, TimeSpan.FromSeconds(2));
                // Set the queue-loss counter deterministically instead of racing a writer thread.
                typeof(JsonLinesEventSink).GetField("droppedCount",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(sink, 1L);
                sink.Flush(TimeSpan.FromSeconds(2));
                Assert.IsTrue(File.Exists(path + ".incomplete"));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        private static StructuredEvent CreateEvent(long sequence)
        {
            return new StructuredEvent(
                1,
                "session",
                sequence,
                "event",
                DateTimeOffset.UtcNow,
                new Dictionary<string, string>());
        }
    }
}
