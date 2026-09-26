using System;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Runtime.FullRun;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace HollowKnightTAS.Core.Tests.FullRun
{
    [TestClass]
    public sealed class WorldObservationTests
    {
        [TestMethod]
        public async Task QueryRunsOnlyOnBoundaryThreadAndPreservesExceptions()
        {
            using var wake = new AutoResetEvent(false);
            using var queue = new FrameObservationQueue(() => wake.Set());
            var task = Task.Run(() => queue.Invoke(frame => (frame, Environment.CurrentManagedThreadId)));
            Assert.IsTrue(wake.WaitOne(3000)); Assert.IsFalse(task.IsCompleted);
            int serviceThread = Environment.CurrentManagedThreadId;
            queue.Service(42);
            Assert.AreEqual((42L, serviceThread), await task);
            var failed = Task.Run(() => queue.Invoke<int>(_ => throw new InvalidOperationException("collector-error")));
            Assert.IsTrue(wake.WaitOne(3000)); queue.Service(42);
            try { await failed; Assert.Fail(); } catch (InvalidOperationException ex) { Assert.AreEqual("collector-error", ex.Message); }
        }

        [TestMethod]
        public async Task ExpiredAndDisposedRequestsDoNotReadUnityLater()
        {
            int reads = 0;
            using var queue = new FrameObservationQueue(() => true);
            try { queue.Invoke(_ => ++reads, 5); Assert.Fail(); } catch (TimeoutException) { }
            queue.Service(99); Assert.AreEqual(0, reads);
            using var wake = new AutoResetEvent(false);
            var closing = new FrameObservationQueue(() => wake.Set());
            var pending = Task.Run(() => closing.Invoke(_ => ++reads));
            Assert.IsTrue(wake.WaitOne(3000)); closing.Dispose();
            try { await pending; Assert.Fail(); } catch (ObjectDisposedException) { }
            closing.Service(100); Assert.AreEqual(0, reads);
        }

        [TestMethod]
        public void SnapshotPagesAreImmutableBoundedAndOversizeIsExplicit()
        {
            var cache = new WorldObservationCache();
            var objects = Enumerable.Range(0, 140).Select(i => Tuple.Create("object" + i, "test",
                new JObject { ["id"] = "object" + i, ["payload"] = new string('x', i == 0 ? 200000 : 2000) }.ToString())).ToArray();
            var id = cache.AddSnapshot(10, 5, "{\"phase\":\"boundary\"}", objects);
            int offset = 0, count = 0;
            while (offset != -1)
            {
                var page = cache.ReadSnapshot(id, offset, 128);
                Assert.AreEqual("10", page["nativeFrame"]);
                Assert.IsTrue(page["snapshotJson"].Length < 161000);
                var root = JObject.Parse(page["snapshotJson"]);
                var items = (JArray)root["objects"]!;
                if (offset == 0) Assert.IsTrue((bool)items[0]["detailsRequired"]!);
                count += items.Count; offset = (int)root["nextOffset"]!;
            }
            Assert.AreEqual(140, count);
            Assert.AreEqual(10L, (long)JObject.Parse(cache.ReadSnapshot(id, 0, 1)["snapshotJson"])["nativeFrame"]!);
            for (int i = 0; i < 4; i++) cache.AddSnapshot(11 + i, 6, "{}", Array.Empty<Tuple<string, string, string>>());
            try { cache.ReadSnapshot(id, 0, 1); Assert.Fail(); } catch (InvalidOperationException ex) { StringAssert.Contains(ex.Message, "expired"); }
        }

        [TestMethod]
        public void DetailsChunksRoundTripUnicodeChecksumAndRejectWrongObjectOrFrame()
        {
            var cache = new WorldObservationCache();
            var original = "{\"text\":\"" + new string('a', 1014) + "😀" + new string('中', 10000) + "\"}";
            var id = cache.AddDetails("hero", 123, 100, original);
            int cursor = 0; var joined = new StringBuilder(); string hash = "";
            while (cursor != -1)
            {
                var page = cache.ReadDetails(id, "hero", 123, cursor, 1024);
                joined.Append(page["detailsJson"]); cursor = int.Parse(page["nextCursor"]); hash = page["sha256"];
                Assert.AreEqual(cursor == -1 ? "true" : "false", page["complete"]);
            }
            Assert.AreEqual(original, joined.ToString());
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original))).ToLowerInvariant(), hash);
            try { cache.ReadDetails(id, "enemy", null, 0, 1024); Assert.Fail(); } catch (InvalidOperationException) { }
            try { cache.ReadDetails(id, "hero", 124, 0, 1024); Assert.Fail(); } catch (InvalidOperationException) { }
        }

        [TestMethod]
        public void MetadataBudgetRejectsUnreadablePagesAndDetailsRespectWireBudget()
        {
            var cache = new WorldObservationCache();
            try
            {
                cache.AddSnapshot(1, 1, "{\"name\":\"" + new string('中', 400000) + "\"}", Array.Empty<Tuple<string, string, string>>());
                Assert.Fail();
            }
            catch (InvalidOperationException ex) { StringAssert.Contains(ex.Message, "metadata"); }
            var id = cache.AddDetails("hero", 1, 1, new string('\0', 250000));
            var chunk = cache.ReadDetails(id, "hero", 1, 0, 200000);
            chunk["requestId"] = new string('x', 128);
            Assert.IsTrue(HollowKnightTAS.Core.Ipc.IpcPayloadCodec.Serialize(chunk).Length < 1024 * 1024);
        }

        [TestMethod]
        public void DisplayRefreshCannotEvictPaginatedWorldQuery()
        {
            var cache = new WorldObservationCache();
            var empty = Array.Empty<Tuple<string, string, string>>();
            var world = cache.AddSnapshot(1, 1, "{}", empty);
            for (int i = 0; i < 20; i++) cache.AddSnapshot(i, i, "{}", empty, "colliders");
            Assert.AreEqual("1", cache.ReadSnapshot(world, 0, 1)["nativeFrame"]);
        }
    }
}
