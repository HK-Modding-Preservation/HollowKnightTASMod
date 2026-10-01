using HollowKnightTAS.Runtime.FullRun;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests;

[TestClass]
public sealed class CinematicPlaybackClockTests
{
    [TestMethod]
    public void ClipEndsAtExactDurationWithoutAccumulatedExtraFrame()
    {
        var clock = new CinematicPlaybackClock(906d / 30);
        clock.Play(0);
        for (var frame = 1; frame < 1510; frame++) clock.Update(frame, 1d / 50, false);
        Assert.IsTrue(clock.Playing);
        clock.Update(1510, 1d / 50, false);
        Assert.IsFalse(clock.Playing);
        Assert.AreEqual(30.2, clock.Elapsed);
    }

    [TestMethod]
    public void PausedAndRepeatedObservationsDoNotConsumeVideoTime()
    {
        var clock = new CinematicPlaybackClock(1);
        clock.Play(100);
        for (var i = 0; i < 1000; i++) clock.Update(100, 0.5, false);
        Assert.AreEqual(0, clock.Elapsed);
        clock.Update(101, 0.25, false);
        Assert.AreEqual(0.25, clock.Elapsed);
        Assert.IsTrue(clock.Playing);
    }

    [TestMethod]
    public void VariableFrameRateAndColdReplayReachSameNaturalEnd()
    {
        var continuous = new CinematicPlaybackClock(3.125);
        var stepped = new CinematicPlaybackClock(3.125);
        continuous.Play(30);
        stepped.Play(9000);
        for (var i = 1; i <= 200; i++)
        {
            var delta = i % 2 == 0 ? 1.0 / 59.94 : 1.0 / 50;
            continuous.Update(30 + i, delta, false);
            stepped.Update(9000 + i, delta, false);
            stepped.Update(9000 + i, delta, false);
            Assert.AreEqual(continuous.Elapsed, stepped.Elapsed);
            Assert.AreEqual(continuous.Playing, stepped.Playing);
        }
        Assert.AreEqual(3.125, continuous.Elapsed);
        Assert.IsFalse(continuous.Playing);
    }

    [TestMethod]
    public void LoopingRetainsRemainderAndSkipDoesNotRestartOnUpdate()
    {
        var clock = new CinematicPlaybackClock(1);
        clock.Play(0);
        clock.Update(1, 2.25, true);
        Assert.AreEqual(0.25, clock.Elapsed);
        Assert.IsTrue(clock.Playing);
        clock.Stop();
        clock.Update(2, 0.5, true);
        Assert.AreEqual(0.25, clock.Elapsed);
        Assert.IsFalse(clock.Playing);
        clock.Play(3);
        Assert.AreEqual(0, clock.Elapsed);
        Assert.IsTrue(clock.Playing);
    }
}
