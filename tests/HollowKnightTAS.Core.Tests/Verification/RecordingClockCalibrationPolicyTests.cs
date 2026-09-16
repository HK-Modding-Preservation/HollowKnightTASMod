using HollowKnightTAS.ClockPayload;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Verification
{
    [TestClass]
    public sealed class RecordingClockCalibrationPolicyTests
    {
        [TestMethod]
        public void EstablishedTimelineCannotRecalibrateOnWorkshopReturn()
        {
            foreach (var requested in new[] { false, true })
            foreach (var fixture in new[] { false, true })
                Assert.IsFalse(RecordingClockCalibrationPolicy.IsAllowed(requested, true, fixture));
        }

        [TestMethod]
        public void PreRootPreparationRetainsExistingEligibility()
        {
            Assert.IsTrue(RecordingClockCalibrationPolicy.IsAllowed(true, false, false));
            Assert.IsTrue(RecordingClockCalibrationPolicy.IsAllowed(true, false, true));
            Assert.IsTrue(RecordingClockCalibrationPolicy.IsAllowed(false, false, true));
            Assert.IsFalse(RecordingClockCalibrationPolicy.IsAllowed(false, false, false));
        }
    }
}
