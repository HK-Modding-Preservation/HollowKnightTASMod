using HollowKnightTAS.Runtime.ReplaySave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.ReplaySave
{
    [TestClass]
    public sealed class ReviewedProtectedModsTests
    {
        [TestMethod]
        public void ExternalCompatibilityRequiresExactReviewedNameAndBinary()
        {
            Assert.IsTrue(ReviewedProtectedMods.Allows("EnviousMarmu", ReviewedProtectedMods.EnviousMarmuSha256));
            Assert.IsFalse(ReviewedProtectedMods.Allows("EnviousMarmu", new string('0', 64)));
            Assert.IsFalse(ReviewedProtectedMods.Allows("UnknownMod", ReviewedProtectedMods.EnviousMarmuSha256));
            Assert.IsFalse(ReviewedProtectedMods.Allows("HollowKnightTAS", ReviewedProtectedMods.EnviousMarmuSha256));
            Assert.IsFalse(ReviewedProtectedMods.Allows("EnviousMarmu", ""));
        }
    }
}
