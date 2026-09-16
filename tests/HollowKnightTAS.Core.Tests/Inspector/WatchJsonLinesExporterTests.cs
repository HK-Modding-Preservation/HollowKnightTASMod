using System;
using System.IO;
using System.Reflection;
using System.Text;
using HollowKnightTAS.Core.Inspector;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Runtime.Inspector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Inspector
{
    [TestClass]
    public sealed class WatchJsonLinesExporterTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

        [TestMethod]
        public void ExactLimitClosesCompleteAndWillNotOverwrite()
        {
            WithPath(path =>
            {
                var frame = Frame();
                var json = WatchFrameJson.Serialize(frame) + "\n";
                using (var exporter = new WatchJsonLinesExporter(path, 4, Timeout, _ => { },
                    Encoding.UTF8.GetByteCount(json)))
                {
                    Assert.IsTrue(File.Exists(path + ".incomplete"));
                    Assert.IsTrue(exporter.TryExport(frame));
                    exporter.Flush(Timeout);
                    Assert.AreEqual(1L, exporter.WrittenCount);
                }
                Assert.AreEqual(json, File.ReadAllText(path));
                Assert.IsFalse(File.Exists(path + ".incomplete"));
                Assert.ThrowsExactly<IOException>(() => new WatchJsonLinesExporter(path, 4, Timeout, _ => { }));
                Assert.AreEqual(json, File.ReadAllText(path));
            });
        }

        [TestMethod]
        public void QuotaStopsWholeRecordsWithoutPressureCallbackAndFlushStillWorks()
        {
            WithPath(path =>
            {
                var pressure = 0;
                using (var exporter = new WatchJsonLinesExporter(path, 4, Timeout, _ => pressure++, 1))
                {
                    Assert.IsTrue(exporter.TryExport(Frame()));
                    exporter.Flush(Timeout);
                    Assert.IsTrue(exporter.SizeLimitReached);
                    Assert.IsFalse(exporter.TryExport(Frame()));
                    exporter.Flush(Timeout);
                    Assert.AreEqual(2L, exporter.DroppedCount);
                    Assert.AreEqual(0L, exporter.WrittenCount);
                    Assert.AreEqual(0, pressure);
                }
                Assert.AreEqual(0L, new FileInfo(path).Length);
                Assert.IsTrue(File.Exists(path + ".incomplete"));
            });
        }

        [TestMethod]
        public void DroppedQueueRecordsPreventCompleteMarkerRemoval()
        {
            WithPath(path =>
            {
                using (var exporter = new WatchJsonLinesExporter(path, 4, Timeout, _ => { }))
                {
                    // Exercise the completion decision without a scheduler-dependent stress test.
                    typeof(WatchJsonLinesExporter).GetField("droppedCount",
                        BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(exporter, 1L);
                    exporter.Flush(Timeout);
                }
                Assert.IsTrue(File.Exists(path + ".incomplete"));
            });
        }

        [TestMethod]
        public void ExistingSessionsConsumeSharedBudget()
        {
            WithPath(path =>
            {
                var root = Path.GetDirectoryName(path)!;
                File.WriteAllText(Path.Combine(root, "old.jsonl"), "1234");
                using (var exporter = new WatchJsonLinesExporter(path, 4, Timeout, _ => { },
                    1000, root, 4))
                {
                    exporter.TryExport(Frame());
                    exporter.Flush(Timeout);
                    Assert.IsTrue(exporter.SizeLimitReached);
                }
                Assert.AreEqual("1234", File.ReadAllText(Path.Combine(root, "old.jsonl")));
                Assert.AreEqual(0L, new FileInfo(path).Length);
            });
        }

        private static WatchFrame Frame()
        {
            using var registry = new WatchRegistry();
            return registry.SampleFresh(new TickStamp(1, 1, 1, 0, TickPhase.LateUpdateEnd), 1);
        }

        private static void WithPath(Action<string> check)
        {
            var root = Path.Combine(Path.GetTempPath(), "hktas-watch-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { check(Path.Combine(root, "watches.jsonl")); }
            finally { Directory.Delete(root, true); }
        }
    }
}
