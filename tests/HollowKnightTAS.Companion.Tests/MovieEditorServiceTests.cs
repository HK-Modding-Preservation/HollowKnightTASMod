using System;
using System.IO;
using HollowKnightTAS.Companion.Services;
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
    }
}
