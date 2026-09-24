using System;
using System.IO;
using HollowKnightTAS.Core.FullRun;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.FullRun
{
    [TestClass]
    public sealed class FullRunBootDescriptorTests
    {
        private const string Token = "0123456789abcdef0123456789abcdef";

        [TestMethod]
        public void RecordDescriptor_RoundTripsWithoutMovieBinding()
        {
            var record = new FullRunBootDescriptor(Token, "run-1", "Record", false,
                string.Empty, string.Empty);
            var parsed = FullRunBootDescriptor.Parse(FullRunBootDescriptor.Serialize(record));
            Assert.AreEqual(Token, parsed.GateToken);
            Assert.AreEqual("run-1", parsed.RunId);
            Assert.AreEqual("Record", parsed.Mode);
            Assert.IsFalse(parsed.MouseEnabled);
            Assert.AreEqual(string.Empty, parsed.MoviePath);
        }

        [TestMethod]
        public void ReplayDescriptor_RequiresAbsoluteMovieAndDigest()
        {
            var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "movie.hktas"));
            var replay = new FullRunBootDescriptor(Token, "run-2", "Replay", true,
                path, new string('a', 64));
            Assert.AreEqual(path, FullRunBootDescriptor.Parse(
                FullRunBootDescriptor.Serialize(replay)).MoviePath);
            Assert.Throws<ArgumentException>(() => new FullRunBootDescriptor(Token,
                "run-2", "Replay", true, "relative.hktas", new string('a', 64)));
            Assert.Throws<ArgumentException>(() => new FullRunBootDescriptor(Token,
                "run-2", "Record", false, path, new string('a', 64)));
        }
    }
}
