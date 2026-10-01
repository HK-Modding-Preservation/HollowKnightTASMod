using System;
using HollowKnightTAS.Core.Inspector;
using HollowKnightTAS.Core.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Inspector
{
    [TestClass]
    public sealed class InfoSequenceTimerTests
    {
        private static InfoLoadSample Sample(InfoGameState game = InfoGameState.Playing, bool uiPlaying = true,
            bool uiPaused = false, string scene = "Room_A", string next = "Room_A", bool input = true,
            bool teleport = false, bool hazard = false, bool waiting = false)
            => new(game, uiPlaying, uiPaused, scene, next, input, teleport, hazard, waiting);

        [TestMethod]
        public void RtMatchesVideoCumulativeTimeAcrossFractionalRatesAndLoads()
        {
            var timer = new InfoSequenceTimer(); var video = new VariableVideoTimeline();
            decimal expectedGt = 0;
            for (int i = 0; i < 10000; i++)
            {
                double seconds = i % 3 == 0 ? 1d / 99.999 : i % 3 == 1 ? 1d / 59.94 : .02;
                bool loading = i % 3 == 2;
                timer.CompleteFrame(seconds, loading); video.Advance(seconds, 48000, 2);
                if (!loading) expectedGt += (decimal)seconds;
                Assert.AreEqual(video.Microseconds / 1000000d, timer.RealSeconds, .00000051);
            }
            Assert.AreEqual((double)expectedGt, timer.GameSeconds, 1e-10);
            double stopped = timer.RealSeconds;
            // Sampling the UI repeatedly never advances either counter.
            for (int i = 0; i < 100; i++) Assert.AreEqual(stopped, timer.RealSeconds);
            var replay = new InfoSequenceTimer(); Assert.AreEqual(0d, replay.RealSeconds);
            Assert.AreEqual(0d, replay.GameSeconds);
        }
        [TestMethod]
        public void GameplayHazardRespawnAndInGamePauseFollowLiveSplit()
        {
            var removal = new InfoLoadRemoval();
            Assert.IsFalse(removal.IsPaused(Sample()));
            Assert.IsTrue(removal.IsPaused(Sample(teleport: true)));
            Assert.IsFalse(removal.IsPaused(Sample(teleport: true, hazard: true)), "Hazard respawn is not a removed teleport load.");
            Assert.IsFalse(removal.IsPaused(Sample(InfoGameState.Other, uiPlaying: false, uiPaused: true)), "An ordinary pause menu still counts.");
            Assert.IsTrue(removal.IsPaused(Sample(InfoGameState.Other, uiPlaying: false, uiPaused: true, input: false)));
        }
        [TestMethod]
        public void MenuToGameplayWaitsForTeleportAndClearsOnAbandonedTransition()
        {
            var removal = new InfoLoadRemoval();
            removal.IsPaused(Sample(InfoGameState.MainMenu, false, scene: "Menu_Title", next: "Room_A"));
            Assert.IsTrue(removal.IsPaused(Sample()));
            Assert.IsTrue(removal.IsPaused(Sample(InfoGameState.EnteringLevel)));
            Assert.IsTrue(removal.IsPaused(Sample(teleport: true)));
            Assert.IsFalse(removal.IsPaused(Sample()));
            removal.IsPaused(Sample(InfoGameState.MainMenu)); removal.IsPaused(Sample());
            removal.IsPaused(Sample(InfoGameState.Loading));
            Assert.IsFalse(removal.IsPaused(Sample()));
        }
        [TestMethod]
        public void RemovesLoadingWaitingUiTransitionsAndQuitToMenu()
        {
            foreach (var sample in new[]
            {
                Sample(InfoGameState.Loading), Sample(InfoGameState.ExitingLevel), Sample(waiting: true),
                Sample(uiPlaying: false), Sample(InfoGameState.EnteringLevel, uiPlaying: false),
                Sample(InfoGameState.Other, false, scene: "Quit_To_Menu", next: "Menu_Title"),
                Sample(InfoGameState.Other, false, scene: "Room_A", next: ""),
                Sample(InfoGameState.Other, false, scene: "_test_charms", next: "")
            }) Assert.IsTrue(new InfoLoadRemoval().IsPaused(sample));
            Assert.IsFalse(new InfoLoadRemoval().IsPaused(Sample(InfoGameState.Other, false, scene: "Menu_Title", next: "")));
        }
        [TestMethod]
        public void TimingPresetsAllowOffsetsAndClampingWithoutChangingCounters()
        {
            object? Read(string key) => key == "rt" ? 15d : key == "gt" ? 8d : null;
            Assert.AreEqual(2.5d, InfoWatchExpression.Parse("rt - 12.5").Evaluate(_ => null, Read));
            Assert.AreEqual(-4.5d, InfoWatchExpression.Parse("gt - 12.5").Evaluate(_ => null, Read));
            Assert.AreEqual(0d, InfoWatchExpression.Parse("gt < 12.5 ? 0 : gt - 12.5").Evaluate(_ => null, Read));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new InfoSequenceTimer().CompleteFrame(double.NaN, false));
        }
    }
}
