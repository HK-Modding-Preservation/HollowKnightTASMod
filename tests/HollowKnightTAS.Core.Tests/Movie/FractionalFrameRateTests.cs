using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HollowKnightTAS.Core.FullRun;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Runtime.FullRun;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Movie
{
    [TestClass]
    public sealed class FractionalFrameRateTests
    {
        private static readonly MovieV2Header Header = new("game", "api", "mod",
            MovieProtocolV2.NativeProfileId, MovieProtocolV2.ActionSchemaId, false, "none", 800, 450);
        private static NativeFrameRun Run(decimal fps, long count = 1) =>
            new(count, Array.Empty<GameInputSample>(), new MovieSourceSpan("test", 1, 1, 1), fps);
        private static MovieV2Document Movie(params NativeFrameRun[] runs) => new("test", Header, runs);

        [TestMethod]
        public void DecimalInputIsExactAndCultureIndependentWithoutSilentRounding()
        {
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                foreach (var text in new[] { "99.999", "99.999000", " 99.999000000 " })
                {
                    var fps = MovieFrameRate.Parse(text);
                    Assert.AreEqual(99.999m, fps);
                    MovieFrameRate.ToRatio(fps, out var n, out var d);
                    Assert.AreEqual(99999, n); Assert.AreEqual(1000, d);
                    Assert.AreEqual(fps, MovieFrameRate.FromRatio(n, d));
                }
                foreach (var text in new[] { "0.999999", "1000.000001", "99,999", "1e2", "NaN", "1.0000001",
                    "99.999000000000000000000000000000000001", "", "-1" })
                    Assert.IsFalse(MovieFrameRate.TryParse(text, out _), text);
                Assert.AreEqual(1.000001m, MovieFrameRate.Parse("1.000001"));
                Assert.AreEqual(1000m, MovieFrameRate.Parse("1000"));
            }
            finally { CultureInfo.CurrentCulture = old; }
        }

        [TestMethod]
        public void CanonicalRatioRoundTripsAndOldIntegerBytesStayIdentical()
        {
            var codec = new MovieV2Codec();
            var old = codec.WriteCanonical(Movie(Run(50), Run(59), Run(1000)));
            StringAssert.Contains(old, "{\"repeatCount\":1,\"samples\":[]}");
            StringAssert.Contains(old, "{\"repeatCount\":1,\"fps\":59,\"samples\":[]}");
            Assert.IsFalse(old.Contains("fpsDenominator"));
            Assert.AreEqual(old, codec.WriteCanonical(codec.Parse(new StringReader(old), "old").Document!));
            var text = codec.WriteCanonical(Movie(Run(99.999m), Run(99.999000m), Run(1.000001m), Run(999.999999m)));
            StringAssert.Contains(text, "\"repeatCount\":2,\"fps\":99999,\"fpsDenominator\":1000");
            var parsed = codec.Parse(new StringReader(text), "fractional");
            Assert.IsTrue(parsed.Success);
            Assert.AreEqual(99.999m, parsed.Document!.Runs[0].FramesPerSecond);
            Assert.AreEqual(text, codec.WriteCanonical(parsed.Document));
            foreach (var fields in new[] { "\"fps\":99999", "\"fpsDenominator\":1000", "\"fps\":1,\"fpsDenominator\":0",
                "\"fps\":4,\"fpsDenominator\":3", "\"fps\":1000001,\"fpsDenominator\":1000" })
            {
                var bad = old.Split('\n')[0] + "\n{\"repeatCount\":1," + fields + ",\"samples\":[]}\n";
                Assert.IsFalse(codec.Parse(new StringReader(bad), "bad").Success, fields);
            }
        }

        [TestMethod]
        public void FractionalEditPreservesPrefixAndSeedThroughSliceAndInsert()
        {
            var original = Movie(Run(50, 10));
            var seeded = MovieV2RangeEditor.SetRngSeed(original, 4, 123);
            var changed = MovieV2RangeEditor.SetFrameRate(seeded, 4, 2, 99.999m);
            Assert.IsTrue(MovieV2Prefix.Matches(seeded, changed, 4));
            Assert.IsFalse(MovieV2Prefix.Matches(seeded, changed, 5));
            Assert.AreEqual(50m, changed.Runs[0].FramesPerSecond);
            Assert.IsTrue(changed.Runs.Skip(1).All(r => r.Authored));
            Assert.AreEqual(123, changed.Runs[1].RngSeed);
            var prefix = MovieV2Prefix.Take(changed, 5);
            Assert.AreEqual(99.999m, prefix.Runs.Last().FramesPerSecond);
            var inserted = new MovieV2TimelineEditor().InsertFrames(original, 0, prefix.Runs);
            Assert.IsTrue(inserted.Success);
            Assert.AreEqual(99.999m, inserted.Movie!.Runs[1].FramesPerSecond);
        }

        [TestMethod]
        public void LegacyIntegerJournalSegmentsRemainReadable()
        {
            var root = Path.Combine(Path.GetTempPath(), "HKTAS-OldJournal-" + Guid.NewGuid().ToString("N"));
            try
            {
                var journal = new FullRunFrameJournal(root);
                journal.CompleteFrame(0, 59);
                journal.Freeze(Header, 1);
                var path = Directory.GetFiles(root, "*.seg", SearchOption.AllDirectories).Single();
                using (var writer = new BinaryWriter(File.Create(path)))
                {
                    writer.Write(0x334a5448); writer.Write(0L); writer.Write(1);
                    writer.Write(0L); writer.Write(59); writer.Write(0);
                }
                var restored = journal.Freeze(Header, 1);
                Assert.AreEqual(59m, restored.Runs.Single().FramesPerSecond);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestMethod]
        public void BootDescriptorAndRecordingJournalRetainExactRateAcrossSegments()
        {
            var descriptor = new FullRunBootDescriptor("0123456789abcdef0123456789abcdef", "fractional-test",
                "Record", false, "", "", 99.999m);
            var bytes = FullRunBootDescriptor.Serialize(descriptor);
            StringAssert.Contains(Encoding.UTF8.GetString(bytes), "\"fpsDenominator\":1000");
            Assert.AreEqual(99.999m, FullRunBootDescriptor.Parse(bytes).FramesPerSecond);
            var root = Path.Combine(Path.GetTempPath(), "HKTAS-Fractional-" + Guid.NewGuid().ToString("N"));
            try
            {
                var journal = new FullRunFrameJournal(root);
                for (var i = 0; i < 130; i++) journal.CompleteFrame(i, 99.999m);
                var first = journal.Freeze(Header, 130);
                Assert.AreEqual(1, first.Runs.Count);
                Assert.AreEqual(130L, first.Runs[0].RepeatCount);
                Assert.AreEqual(99.999m, first.Runs[0].FramesPerSecond);
                journal.CompleteFrame(130, 50);
                var second = journal.Freeze(Header, 131);
                Assert.AreEqual(2, second.Runs.Count);
                Assert.AreEqual(50m, second.Runs[1].FramesPerSecond);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
