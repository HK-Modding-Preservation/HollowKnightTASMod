using System;
using System.IO;
using System.Linq;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Movie
{
    [TestClass]
    public sealed class MovieRngSeedTests
    {
        private static readonly MovieSourceSpan Span = new MovieSourceSpan("rng", 1, 1, 1);
        private static MovieV2Document Movie(params NativeFrameRun[] runs) => new MovieV2Document("rng",
            new MovieV2Header("game", "api", "mod", MovieProtocolV2.NativeProfileId,
                MovieProtocolV2.ActionSchemaId, false, new string('a', 64), 800, 450),
            runs.Length == 0 ? new[] { new NativeFrameRun(10, Array.Empty<GameInputSample>(), Span) } : runs);
        private static string Text(MovieV2Document movie) => new MovieV2Codec().WriteCanonical(movie);

        [TestMethod]
        public void SeedsRoundTripAndNeverMergeIncludingZeroAndExtremes()
        {
            var seeds = new[] { 0, 0, int.MinValue, int.MaxValue };
            var movie = Movie(seeds.Select(s => new NativeFrameRun(1, Array.Empty<GameInputSample>(), Span, rngSeed: s)).ToArray());
            var parsed = new MovieV2Codec().Parse(new StringReader(Text(movie)), "test");
            Assert.IsTrue(parsed.Success);
            Assert.AreEqual(4, parsed.Document!.Runs.Count);
            CollectionAssert.AreEqual(seeds, parsed.Document.Runs.Select(r => r.RngSeed!.Value).ToArray());
            Assert.AreEqual(Text(movie), Text(parsed.Document));
        }

        [TestMethod]
        public void SeedRunMustBeSingleFrameInParserWriterAndValidator()
        {
            var movie = Movie(new NativeFrameRun(2, Array.Empty<GameInputSample>(), Span, rngSeed: 5));
            Assert.IsFalse(new MovieV2Validator().Validate(movie, MovieV2ValidationContext.CreateDefault()).Success);
            Assert.ThrowsExactly<InvalidDataException>(() => Text(movie));
            var text = Text(Movie(new NativeFrameRun(1, Array.Empty<GameInputSample>(), Span, rngSeed: 5)))
                .Replace("\"repeatCount\":1", "\"repeatCount\":2");
            Assert.IsFalse(new MovieV2Codec().Parse(new StringReader(text), "test").Success);
        }

        [TestMethod]
        public void InvalidSeedTypesAndOverflowAreRejected()
        {
            var text = Text(MovieV2RangeEditor.SetRngSeed(Movie(), 0, 5));
            foreach (var value in new[] { "null", "\"5\"", "1.5", "2147483648", "-2147483649", "true" })
                Assert.IsFalse(new MovieV2Codec().Parse(new StringReader(text.Replace("\"rngSeed\":5", "\"rngSeed\":" + value)), "test").Success, value);
        }

        [TestMethod]
        public void PrefixDivergesOnlyAfterSeededFrameAndClearKeepsSuffixAuthored()
        {
            var original = Movie();
            var edited = MovieV2RangeEditor.SetRngSeed(original, 4, 0);
            Assert.IsTrue(MovieV2Prefix.Matches(original, edited, 4));
            Assert.IsFalse(MovieV2Prefix.Matches(original, edited, 5));
            var cleared = MovieV2RangeEditor.SetRngSeed(edited, 4, null);
            Assert.AreEqual(Text(MovieV2RangeEditor.AuthorFrom(original, 4)), Text(cleared));
            Assert.IsTrue(MovieV2Prefix.Take(edited, 4).Runs.All(r => !r.Authored));
            Assert.IsTrue(edited.Runs.Skip(1).All(r => r.Authored));
            Assert.AreEqual(Text(original), Text(MovieV2RangeEditor.SetRngSeed(original, 4, null)));
            Assert.IsFalse(Text(original).Contains("rngSeed"));
        }

        [TestMethod]
        public void CausalEditsPreservePrefixAndSuffixValuesButRetireOldAssertions()
        {
            var values = new short[26]; values[15] = short.MaxValue;
            var original = Movie(new NativeFrameRun(10,
                new[] { new GameInputSample(GameInputChannel.Hero, values, null, 1UL << 15, 0) }, Span));
            var editor = new MovieV2TimelineEditor();
            var edits = new[] {
                MovieV2RangeEditor.SetRngSeed(original, 4, 0),
                MovieV2RangeEditor.SetFrameRate(original, 4, 1, 120),
                MovieV2RangeEditor.Paint(original, 4, 1, "Jump", true),
                editor.ReplaceFrame(original, 4, original.Runs[0].Samples).Movie,
                editor.InsertFrames(original, 4, new[] { new NativeFrameRun(1, Array.Empty<GameInputSample>(), Span) }).Movie,
                editor.DeleteFrames(original, 4, 1).Movie
            };
            foreach (var edited in edits)
            {
                Assert.IsTrue(MovieV2Prefix.Matches(original, edited, 4));
                var roundTrip = new MovieV2Codec().Parse(new StringReader(Text(edited)), "branch").Document!;
                Assert.IsTrue(roundTrip.Runs.Skip(1).All(r => r.Authored));
                Assert.AreEqual(short.MaxValue, roundTrip.Runs.Last().Samples.Single().Values[15]);
            }
            Assert.IsFalse(original.Runs[0].Authored);
        }

        [TestMethod]
        public void InputAndFpsEditsKeepSeedAndOtherFrameData()
        {
            var seeded = MovieV2RangeEditor.SetRngSeed(Movie(), 4, -17);
            var painted = MovieV2RangeEditor.Paint(seeded, 2, 5, "Attack", true);
            var fps = MovieV2RangeEditor.SetFrameRate(painted, 0, 10, 120);
            var replaced = new MovieV2TimelineEditor().ReplaceFrame(fps, 4, Array.Empty<GameInputSample>());
            Assert.IsTrue(replaced.Success);
            var run = replaced.Movie.Runs.Single(r => r.RngSeed.HasValue);
            Assert.AreEqual(-17, run.RngSeed);
            Assert.AreEqual(120, run.FramesPerSecond);
            Assert.IsTrue(run.Authored);
            Assert.AreEqual(1L, run.RepeatCount);
            Assert.AreEqual(10L, replaced.Movie.Runs.Sum(r => r.RepeatCount));
        }

        [TestMethod]
        public void InsertDeleteAndCopyPreserveSeedFrameAttachment()
        {
            var editor = new MovieV2TimelineEditor();
            var seeded = MovieV2RangeEditor.SetRngSeed(Movie(), 4, 123);
            var inserted = editor.InsertFrames(seeded, 2, new[] { new NativeFrameRun(3, Array.Empty<GameInputSample>(), Span) }).Movie;
            Assert.IsTrue(MovieV2Prefix.Take(inserted, 7).Runs.All(r => !r.RngSeed.HasValue));
            Assert.AreEqual(123, MovieV2Prefix.Take(inserted, 8).Runs.Last().RngSeed);
            var removed = editor.DeleteFrames(inserted, 7, 1).Movie;
            Assert.IsTrue(removed.Runs.All(r => !r.RngSeed.HasValue));
            var copy = editor.InsertFrames(seeded, 5, new[] { seeded.Runs.Single(r => r.RngSeed.HasValue) }).Movie;
            Assert.AreEqual(2, copy.Runs.Count(r => r.RngSeed == 123));
            Assert.AreEqual(11L, copy.Runs.Sum(r => r.RepeatCount));
        }
    }
}
