using System;
using System.Linq;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows.Media;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class ColliderOverlayTests
    {
        [TestMethod]
        public void DecoderReadsNestedScreenPathsAndClassification()
        {
            var json = "{\"nextOffset\":128,\"objects\":[{\"id\":\"hero\",\"kind\":\"player\",\"colliders\":[{\"classification\":\"knight\",\"screenPaths\":[{\"closed\":true,\"points\":[{\"x\":0.1,\"y\":0.2,\"z\":0},{\"x\":0.3,\"y\":0.2,\"z\":0}]}]}]}]}";
            var snapshot = ColliderOverlayDecoder.Decode("s1", json);

            Assert.AreEqual("s1", snapshot.SnapshotId);
            Assert.AreEqual(128, snapshot.NextOffset);
            Assert.AreEqual(1, snapshot.Objects.Count);
            Assert.AreEqual("knight", snapshot.Objects[0].Classification);
            Assert.IsTrue(snapshot.Objects[0].Paths[0].Closed);
            Assert.AreEqual(0.3, snapshot.Objects[0].Paths[0].Points[1].X, 0.00001);
        }

        [TestMethod]
        public void DecoderDoesNotUseWorldPathsFallback()
        {
            var json = "{\"objects\":[{\"id\":\"enemy\",\"classification\":\"enemy\",\"colliders\":[{\"worldPaths\":[{\"closed\":true,\"points\":[{\"x\":0,\"y\":0,\"z\":0},{\"x\":1,\"y\":1,\"z\":0}]}]}]}]}";
            var snapshot = ColliderOverlayDecoder.Decode("s2", json);

            Assert.AreEqual(0, snapshot.Objects.Count);
        }

        [TestMethod]
        public void DecoderClampsNormalizedCoordinatesAndSkipsInvalidPaths()
        {
            var json = "{\"objects\":[{\"id\":\"enemy\",\"classification\":\"enemy\",\"colliders\":[{\"screenPaths\":[{\"points\":[{\"x\":-1,\"y\":2},{\"x\":1.5,\"y\":0}]} ,{\"points\":[{\"x\":\"bad\",\"y\":0},{\"x\":0,\"y\":0}]}]}]}]}";
            var snapshot = ColliderOverlayDecoder.Decode("s3", json);

            Assert.AreEqual(1, snapshot.Objects.Count);
            var points = snapshot.Objects.Single().Paths.Single().Points;
            Assert.AreEqual(0, points[0].X, 0.00001);
            Assert.AreEqual(1, points[0].Y, 0.00001);
            Assert.AreEqual(1, points[1].X, 0.00001);
        }

        [TestMethod]
        public void AppendRequiresStableSnapshotId()
        {
            var first = ColliderOverlayDecoder.Decode("s4", "{\"objects\":[]}");
            var second = ColliderOverlayDecoder.Decode("s4", "{\"objects\":[]}");
            Assert.AreEqual(0, first.Append(second).Objects.Count);
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                first.Append(ColliderOverlayDecoder.Decode("other", "{\"objects\":[]}")));
        }

        [TestMethod]
        public void DebugModCategoryColorsAreStable()
        {
            Assert.AreEqual(Colors.Yellow, ColliderOverlayColors.For("knight"));
            Assert.AreEqual(Colors.Red, ColliderOverlayColors.For("enemy"));
            Assert.AreEqual(Colors.Cyan, ColliderOverlayColors.For("attack"));
            Assert.AreEqual(Colors.LimeGreen, ColliderOverlayColors.For("terrain"));
            Assert.AreEqual(Colors.LightBlue, ColliderOverlayColors.For("trigger"));
            Assert.AreEqual(Colors.MediumPurple, ColliderOverlayColors.For("hazard"));
            Assert.AreEqual(Colors.Orange, ColliderOverlayColors.For("unknown"));
        }
    }
}
