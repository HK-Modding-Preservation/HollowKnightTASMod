using System;
using System.IO;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class MovieEditorServiceTests
    {
        [TestMethod]
        public void ValidateFormatsCanonicalFixture()
        {
            var path = Path.Combine(
                AppContext.BaseDirectory,
                "fixtures",
                "actions-v1.canonical.hktas");
            var source = File.ReadAllText(path);

            var result =
                new MovieEditorService().Validate(
                    source,
                    path);

            Assert.IsTrue(result.Success);
            Assert.IsNotNull(result.Document);
            Assert.AreEqual(source, result.CanonicalText);
            Assert.AreEqual(25L, result.ExpandedTicks);
            Assert.AreEqual(64, result.MovieId.Length);
            Assert.AreEqual(0, result.Diagnostics.Count);
        }

        [TestMethod]
        public void ValidateRejectsUnknownAction()
        {
            var path = Path.Combine(
                AppContext.BaseDirectory,
                "fixtures",
                "unknown-action.hktas");

            var result =
                new MovieEditorService().Validate(
                    File.ReadAllText(path),
                    path);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Diagnostics.Count > 0);
        }

        [TestMethod]
        public void ValidateAny_RoutesV2WithoutChangingLegacyValidate()
        {
            var header = new MovieV2Header("game", "api", "mod", "profile",
                MovieProtocolV2.ActionSchemaId, false, new string('a', 64), 800, 450);
            var movie = new MovieV2Document("v2.hktas", header, new[]
            {
                new NativeFrameRun(2, Array.Empty<GameInputSample>(),
                    new MovieSourceSpan("v2.hktas", 2, 1, 1))
            });
            var source = new MovieV2Codec().WriteCanonical(movie);
            var v2 = new MovieEditorService().ValidateAny(source, "v2.hktas");
            Assert.IsTrue(v2.Success);
            Assert.AreEqual(2, v2.Version);
            Assert.AreEqual(2L, v2.ExpandedFrames);
            Assert.IsNotNull(v2.V2Document);
            Assert.IsNull(v2.V1Document);
            Assert.AreEqual(source, v2.CanonicalText);

            var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "actions-v1.canonical.hktas");
            var v1 = new MovieEditorService().ValidateAny(File.ReadAllText(path), path);
            Assert.IsTrue(v1.Success);
            Assert.AreEqual(1, v1.Version);
            Assert.IsNotNull(v1.V1Document);
            Assert.IsNull(v1.V2Document);
        }
    }
}
