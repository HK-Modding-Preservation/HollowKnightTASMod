using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Movie
{
    [TestClass]
    public sealed class MovieParserValidatorTests
    {
        [TestMethod]
        public void ValidFixtures_ParseValidateAndMatchGolden()
        {
            foreach (var name in new[] { "minimal-v1.hktas", "actions-v1.hktas" })
            {
                var parse = ParseFixture("valid", name);
                Assert.IsTrue(
                    parse.Success,
                    string.Join(Environment.NewLine, parse.Diagnostics.Select(value => value.ToString())));
                var report = new MovieValidator().Validate(
                    parse.Document!,
                    MovieValidationContext.CreateDefault());
                Assert.IsTrue(
                    report.Success,
                    string.Join(Environment.NewLine, report.Diagnostics.Select(value => value.ToString())));
            }

            var actions = ParseFixture("valid", "actions-v1.hktas").Document!;
            var canonical = new MovieCanonicalWriter().WriteToString(actions);
            var golden = File.ReadAllText(
                FixturePath("golden", "actions-v1.canonical.hktas"),
                new UTF8Encoding(false, true));
            Assert.AreEqual(golden, canonical);
        }

        [TestMethod]
        [DataRow("duplicate-header.hktas", MovieDiagnosticCodes.DuplicateHeader, 3, 1)]
        [DataRow("unknown-command.hktas", MovieDiagnosticCodes.UnknownCommand, 8, 1)]
        [DataRow("direction-conflict.hktas", MovieDiagnosticCodes.DirectionConflict, 8, 1)]
        [DataRow("axis-direction-mix.hktas", MovieDiagnosticCodes.AxisDirectionMix, 8, 1)]
        [DataRow("unknown-semantic-path.hktas", MovieDiagnosticCodes.UnknownSemanticPath, 8, 1)]
        [DataRow("invalid-hash.hktas", MovieDiagnosticCodes.InvalidHash, 4, 17)]
        [DataRow("unterminated-marker.hktas", MovieDiagnosticCodes.UnterminatedString, 8, 8)]
        [DataRow("unknown-header.hktas", MovieDiagnosticCodes.UnknownHeader, 2, 1)]
        [DataRow("missing-header.hktas", MovieDiagnosticCodes.MissingHeader, 1, 1)]
        [DataRow("unknown-action.hktas", MovieDiagnosticCodes.UnknownAction, 8, 15)]
        [DataRow("duplicate-field.hktas", MovieDiagnosticCodes.DuplicateField, 8, 17)]
        public void InvalidFixtures_ReportExpectedFirstDiagnostic(
            string name,
            string expectedCode,
            int expectedLine,
            int expectedColumn)
        {
            var parse = ParseFixture("invalid", name);
            MovieDiagnostic diagnostic;
            if (!parse.Success)
            {
                diagnostic = parse.Diagnostics[0];
            }
            else
            {
                var report = new MovieValidator().Validate(
                    parse.Document!,
                    MovieValidationContext.CreateDefault());
                Assert.IsFalse(report.Success);
                diagnostic = report.Diagnostics[0];
            }

            Assert.AreEqual(expectedCode, diagnostic.Code);
            Assert.AreEqual(expectedLine, diagnostic.Span.Line);
            Assert.AreEqual(expectedColumn, diagnostic.Span.Column);
            Assert.IsTrue(diagnostic.Span.Length > 0);
            Assert.IsFalse(string.IsNullOrWhiteSpace(diagnostic.Action));
        }

        [TestMethod]
        public void ParseCanonicalWriteParse_IsStableForOneHundredIterations()
        {
            var document = ParseFixture("valid", "actions-v1.hktas").Document!;
            var writer = new MovieCanonicalWriter();
            var expected = writer.WriteToString(document);
            var expectedId = writer.ComputeMovieId(document);

            for (var iteration = 0; iteration < 100; iteration++)
            {
                using var reader = new StringReader(expected);
                var parse = new MovieParser().Parse(reader, "roundtrip.hktas");
                Assert.IsTrue(parse.Success);
                Assert.AreEqual(expected, writer.WriteToString(parse.Document!));
                Assert.AreEqual(expectedId, writer.ComputeMovieId(parse.Document!));
                document = parse.Document!;
            }
        }

        [TestMethod]
        public void CommentsWhitespaceHeaderOrderAndRleShape_DoNotChangeMovieId()
        {
            var canonicalSource = Header()
                                  + "frames 10 hold=jump\n";
            var variedSource =
                "# comment\n"
                + "api 1.5.78.11833-77\n"
                + " hktas   1  # version\n"
                + "baseline none none\n"
                + "tick-unit input\n"
                + "manifest-sha256 "
                + new string('a', 64)
                + "\n"
                + "game 1.5.78.11833\n"
                + "---\n"
                + "frames 4 hold=jump\n"
                + "\n"
                + "frames 6 hold=jump # merged canonically\n";

            var first = Parse(canonicalSource);
            var second = Parse(variedSource);
            Assert.IsTrue(first.Success);
            Assert.IsTrue(second.Success);

            var writer = new MovieCanonicalWriter();
            Assert.AreEqual(
                writer.ComputeMovieId(first.Document!),
                writer.ComputeMovieId(second.Document!));
            Assert.AreEqual(
                writer.WriteToString(first.Document!),
                writer.WriteToString(second.Document!));
        }

        [TestMethod]
        public void ValidationContext_RejectsManifestBaselineAndExpandedLimit()
        {
            var parse = ParseFixture("valid", "actions-v1.hktas");
            var context = new MovieValidationContext(
                24,
                MovieProtocolV1.DefaultSemanticPaths,
                new string('c', 64),
                new string('d', 64));

            var report = new MovieValidator().Validate(parse.Document!, context);

            CollectionAssert.IsSubsetOf(
                new[]
                {
                    MovieDiagnosticCodes.ManifestMismatch,
                    MovieDiagnosticCodes.BaselineMismatch,
                    MovieDiagnosticCodes.ExpandedTickLimit
                },
                report.Diagnostics.Select(value => value.Code).ToArray());
            Assert.AreEqual(25L, report.ExpandedTickCount);
        }

        [TestMethod]
        public void MarkerLimit_UsesUtf8Bytes()
        {
            var source = Header() + "marker \"" + new string('界', 342) + "\"\n";
            var parse = Parse(source);
            Assert.IsTrue(parse.Success);

            var report = new MovieValidator().Validate(
                parse.Document!,
                MovieValidationContext.CreateDefault());

            Assert.IsFalse(report.Success);
            Assert.AreEqual(MovieDiagnosticCodes.MarkerTooLarge, report.Diagnostics[0].Code);
        }

        [TestMethod]
        public void CanonicalWriter_IsCultureAndPlatformNewlineIndependent()
        {
            var document = ParseFixture("valid", "actions-v1.hktas").Document!;
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar-SA");
                var canonical = new MovieCanonicalWriter().WriteToString(document);

                Assert.IsFalse(canonical.Contains("\r", StringComparison.Ordinal));
                StringAssert.Contains(canonical, "frames 3 hold=attack x=2500 y=-7500\n");
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        [TestMethod]
        public void RandomMalformedInputs_DoNotThrowOrHang()
        {
            var random = new Random(19770615);
            var parser = new MovieParser();
            var stopwatch = Stopwatch.StartNew();
            for (var iteration = 0; iteration < 2000; iteration++)
            {
                var length = random.Next(0, 513);
                var builder = new StringBuilder(length);
                for (var index = 0; index < length; index++)
                {
                    builder.Append((char)random.Next(0, 128));
                }

                using var reader = new StringReader(builder.ToString());
                var result = parser.Parse(reader, "fuzz-" + iteration + ".hktas");
                Assert.IsNotNull(result);
            }

            stopwatch.Stop();
            Assert.IsTrue(
                stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                "Bounded fuzz loop took " + stopwatch.Elapsed + ".");
        }

        [TestMethod]
        public void OverlongLine_IsRejectedBeforeTokenParsing()
        {
            var source = new string('x', MovieParser.MaximumLineCharacters + 1);
            var parse = Parse(source);

            Assert.IsFalse(parse.Success);
            Assert.AreEqual(MovieDiagnosticCodes.LineTooLong, parse.Diagnostics[0].Code);
            Assert.AreEqual(MovieParser.MaximumLineCharacters + 1, parse.Diagnostics[0].Span.Column);
        }

        private static MovieParseResult ParseFixture(string category, string name)
        {
            var path = FixturePath(category, name);
            using var reader = new StreamReader(
                path,
                new UTF8Encoding(false, true),
                false);
            return new MovieParser().Parse(reader, path);
        }

        private static MovieParseResult Parse(string source)
        {
            using var reader = new StringReader(source);
            return new MovieParser().Parse(reader, "inline.hktas");
        }

        private static string FixturePath(string category, string name)
        {
            return Path.Combine(
                AppContext.BaseDirectory,
                "fixtures",
                "movie",
                category,
                name);
        }

        private static string Header()
        {
            return "hktas 1\n"
                   + "game 1.5.78.11833\n"
                   + "api 1.5.78.11833-77\n"
                   + "manifest-sha256 "
                   + new string('a', 64)
                   + "\n"
                   + "baseline none none\n"
                   + "tick-unit input\n"
                   + "---\n";
        }
    }
}
