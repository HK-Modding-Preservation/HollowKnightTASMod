using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Movie
{
    [TestClass]
    public sealed class MovieV2CodecTests
    {
        private const string Header = "{\"format\":\"hktas\",\"version\":2,\"tickUnit\":\"input-playerloop\",\"actionSchemaId\":\"hktas-full-run-actions-v2\",\"nativeProfileId\":\"test-profile\",\"mouseEnabled\":true,\"gameVersion\":\"unknown\",\"apiVersion\":\"unknown\",\"modVersion\":\"unknown\",\"environmentSha256\":\"none\",\"viewportWidth\":0,\"viewportHeight\":0}";

        [TestMethod]
        public void RoundTrip_MergesRunsAndHashesExactCanonicalBytes()
        {
            var values = new short[MovieProtocolV2.HeroActionNames.Count];
            values[10] = short.MaxValue;
            var hero = new GameInputSample(GameInputChannel.Hero, values, null);
            var mouse = new GameInputSample(GameInputChannel.MouseInControl, Array.Empty<short>(),
                new MouseFrameState(65535, 0, -17, 9, 3, -1));
            var span = new MovieSourceSpan("input.hktas", 2, 1, 1);
            var movie = new MovieV2Document("input.hktas", NewHeader(), new[]
            {
                new NativeFrameRun(2, Array.Empty<GameInputSample>(), span),
                new NativeFrameRun(3, Array.Empty<GameInputSample>(), span),
                new NativeFrameRun(1, new[] { hero, mouse }, span)
            });
            var codec = new MovieV2Codec();
            var canonical = codec.WriteCanonical(movie);

            StringAssert.StartsWith(canonical, Header + "\n{\"repeatCount\":5,\"samples\":[]}\n");
            Assert.IsTrue(canonical.EndsWith("\n", StringComparison.Ordinal));
            Assert.IsFalse(canonical.Contains("\r"));
            Assert.AreEqual(3, canonical.Count(character => character == '\n'));
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(canonical))).ToLowerInvariant(),
                codec.ComputeMovieId(movie));

            var result = codec.Parse(new StringReader(canonical), "roundtrip.hktas");
            Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
            Assert.AreEqual(2, result.Document!.Runs.Count);
            Assert.AreEqual(5L, result.Document.Runs[0].RepeatCount);
            Assert.AreEqual(-17, result.Document.Runs[1].Samples[1].Mouse!.DeltaXQ15);
            Assert.AreEqual(canonical, codec.WriteCanonical(result.Document));
            Assert.AreEqual(codec.ComputeMovieId(movie), codec.ComputeMovieId(result.Document));
        }

        [TestMethod]
        public void EquivalentFieldOrderAndWhitespace_HaveTheSameMovieId()
        {
            var a = Parse(Header + "\n{\"repeatCount\":4,\"samples\":[]}\n");
            var b = Parse("{\"gameVersion\":\"unknown\",\"apiVersion\":\"unknown\",\"modVersion\":\"unknown\",\"format\":\"hktas\",\"version\":2,\"tickUnit\":\"input-playerloop\",\"actionSchemaId\":\"hktas-full-run-actions-v2\",\"nativeProfileId\":\"test-profile\",\"mouseEnabled\":true,\"environmentSha256\":\"none\",\"viewportWidth\":0,\"viewportHeight\":0}\r\n"
                + " { \"samples\" : [ ], \"repeatCount\" : 1 }\r\n"
                + "{\"repeatCount\":3,\"samples\":[]}\r\n");
            Assert.AreEqual(new MovieV2Codec().ComputeMovieId(a), new MovieV2Codec().ComputeMovieId(b));
        }

        [TestMethod]
        public void Parser_RejectsDuplicateUnknownAndNonIntegerFields()
        {
            CheckFailure(Header.Replace("\"version\":2", "\"version\":2,\"version\":2") + "\n",
                MovieDiagnosticCodes.DuplicateField);
            CheckFailure(Header + "\n{\"repeatCount\":1,\"samples\":[],\"extra\":1}\n",
                MovieDiagnosticCodes.InvalidCommand);
            CheckFailure(Header + "\n{\"repeatCount\":1.0,\"samples\":[]}\n",
                MovieDiagnosticCodes.InvalidCommand);
            CheckFailure(Header + "\n{\"repeatCount\":NaN,\"samples\":[]}\n",
                MovieDiagnosticCodes.InvalidSyntax);
            CheckFailure(Header + "\n{\"repeatCount\":1,\"samples\":[{\"channel\":\"Hero\",\"values\":[],\"pressedMask\":0,\"releasedMask\":0,\"mouse\":null}]}\n",
                MovieDiagnosticCodes.UnknownAction);
            CheckFailure(Header + "\n{\"repeatCount\":1,\"samples\":[]}\n\uFEFF",
                MovieDiagnosticCodes.InvalidSyntax);
        }

        [TestMethod]
        public void Parser_EnforcesNativeFrameAndLineBounds()
        {
            CheckFailure(Header + "\n{\"repeatCount\":10000000,\"samples\":[]}\n"
                + "{\"repeatCount\":1,\"samples\":[]}\n", MovieDiagnosticCodes.ExpandedTickLimit);
            CheckFailure(Header + "\n" + new string(' ', MovieProtocolV2.MaximumLineCharacters + 1),
                MovieDiagnosticCodes.LineTooLong);
            CheckFailure(Header.Replace("\"environmentSha256\":\"none\"", "\"environmentSha256\":\"bad\"") + "\n",
                MovieDiagnosticCodes.InvalidHeaderValue);
            var one = "{\"channel\":\"mouseInControl\",\"values\":[],\"mouse\":{\"xQ16\":0,\"yQ16\":0,\"deltaXQ15\":0,\"deltaYQ15\":0,\"buttons\":0,\"wheelQ15\":0}}";
            CheckFailure(Header + "\n{\"repeatCount\":1,\"samples\":[" + string.Join(",", Enumerable.Repeat(one, 257)) + "]}\n",
                MovieDiagnosticCodes.InvalidCommand);
            CheckFailure("\uFEFF" + Header + "\n", MovieDiagnosticCodes.InvalidCharacter);
            var paddedRun = "{\"repeatCount\":1,\"samples\":[]}"
                + new string(' ', MovieProtocolV2.MaximumLineCharacters - 40) + "\n";
            CheckFailure(Header + "\n" + string.Concat(Enumerable.Repeat(paddedRun, 260)),
                MovieDiagnosticCodes.SourceTooLarge);
        }

        [TestMethod]
        public void MaximumAllowedHeroSamples_StillParseWithinJsonNodeLimit()
        {
            var hero = new GameInputSample(GameInputChannel.Hero,
                new short[MovieProtocolV2.HeroActionNames.Count], null);
            var run = new NativeFrameRun(1,
                Enumerable.Repeat(hero, MovieProtocolV2.MaximumSamplesPerFrame).ToArray(),
                new MovieSourceSpan("maximum.hktas", 2, 1, 1));
            var movie = new MovieV2Document("maximum.hktas", NewHeader(), new[] { run });
            var canonical = new MovieV2Codec().WriteCanonical(movie);
            Assert.AreEqual(MovieProtocolV2.MaximumSamplesPerFrame, Parse(canonical).Runs[0].Samples.Count);
        }

        [TestMethod]
        public void Model_CollectionsAreSnapshots()
        {
            var values = new short[6];
            var sample = new GameInputSample(GameInputChannel.PreMenu, values, null);
            values[0] = 4;
            Assert.AreEqual(0, sample.Values[0]);
            var samples = new List<GameInputSample> { sample };
            var run = new NativeFrameRun(1, samples, new MovieSourceSpan("snapshot", 1, 1, 1));
            samples.Clear();
            Assert.AreEqual(1, run.Samples.Count);
            var runs = new List<NativeFrameRun> { run };
            var movie = new MovieV2Document("snapshot", NewHeader(), runs);
            runs.Clear();
            Assert.AreEqual(1, movie.Runs.Count);
        }

        private static MovieV2Header NewHeader() => new MovieV2Header("unknown", "unknown", "unknown", "test-profile",
            MovieProtocolV2.ActionSchemaId, true, "none", 0, 0);

        private static MovieV2Document Parse(string source)
        {
            var result = new MovieV2Codec().Parse(new StringReader(source), "test.hktas");
            Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
            return result.Document!;
        }

        private static void CheckFailure(string source, string expectedCode)
        {
            var result = new MovieV2Codec().Parse(new StringReader(source), "invalid.hktas");
            Assert.IsFalse(result.Success);
            Assert.IsNull(result.Document);
            Assert.AreEqual(expectedCode, result.Diagnostics[0].Code, result.Diagnostics[0].ToString());
        }
    }
}
