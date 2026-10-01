using System;
using HollowKnightTAS.Core.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Media
{
    [TestClass]
    public sealed class InfoOverlayPixelsTests
    {
        [TestMethod]
        public void AlphaCompositeRespectsUnityBottomUpRgbAndPanelTopDownBgra()
        {
            var rgb = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120 };
            var bgra = new byte[] { 0, 0, 255, 255, 0, 64, 0, 128 };
            InfoOverlayPixels.Blend(rgb, 2, 2, bgra, 1, 2, 4, 1, 0);
            CollectionAssert.AreEqual(new byte[] { 10, 20, 30, 20, 89, 30, 70, 80, 90, 255, 0, 0 }, rgb);
        }
        [TestMethod]
        public void TransparentOverlayIsIdentityAndInvalidBoundsAreRejected()
        {
            var rgb = new byte[] { 10, 20, 30 }; var original = (byte[])rgb.Clone();
            InfoOverlayPixels.Blend(rgb, 1, 1, new byte[4], 1, 1, 4, 0, 0);
            CollectionAssert.AreEqual(original, rgb);
            Assert.ThrowsExactly<ArgumentException>(() => InfoOverlayPixels.Blend(rgb, 1, 1, new byte[4], 1, 1, 4, 1, 0));
        }
    }
}
