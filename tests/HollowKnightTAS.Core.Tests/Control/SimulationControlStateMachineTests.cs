using System;
using System.Collections.Generic;
using HollowKnightTAS.Core.Control;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.Playback;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Control
{
    [TestClass]
    public sealed class SimulationControlStateMachineTests
    {
        private const string Manifest =
            "f9966db6db0f9f31958f000959301fd4fc54f5282d07c3afe2b94a93f3baee53";
        private const string Baseline =
            "8b8c7995768198cdac8b44a563e875a50bb47efc70a25eab2e7afabccbd262b9";

        [TestMethod]
        public void MovieTickStep_ClosesGateAtExactQuotaAndReturnsPaused()
        {
            var machine = Pause();
            var request = new StepRequest(StepBoundary.MovieTick, 2);

            Assert.IsTrue(machine.RequestStep(request).Success);
            Assert.IsTrue(machine.MovieTickGateOpen);
            Assert.IsTrue(machine.CommitMovieTick().Success);
            Assert.IsTrue(machine.MovieTickGateOpen);
            Assert.IsFalse(machine.StepQuotaSatisfied);
            Assert.IsTrue(machine.CommitMovieTick().Success);
            Assert.IsFalse(machine.MovieTickGateOpen);
            Assert.IsTrue(machine.StepQuotaSatisfied);
            Assert.IsFalse(machine.CommitMovieTick().Success);

            var result = new StepResult(
                request,
                2,
                3,
                1,
                2,
                Array.Empty<TickLedgerRecord>());
            Assert.IsTrue(machine.CompleteStep(result).Success);
            Assert.AreEqual(SimulationControlMode.Paused, machine.Mode);
            Assert.IsNull(machine.ActiveRequest);
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(4)]
        public void InterruptedStepClosesGateWithoutClaimingQuotaCompletion(int committed)
        {
            var machine = Pause();
            var request = new StepRequest(StepBoundary.MovieTick, 5);
            Assert.IsTrue(machine.RequestStep(request).Success);
            for (var i = 0; i < committed; i++) Assert.IsTrue(machine.CommitMovieTick().Success);
            Assert.AreEqual(committed, machine.CommittedMovieTicks);
            Assert.IsFalse(machine.CompleteStep(new StepResult(request, committed, committed,
                0, (ulong)committed, Array.Empty<TickLedgerRecord>())).Success);
            Assert.IsTrue(machine.InterruptStepAtCompletedBoundary().Success);
            Assert.AreEqual(SimulationControlMode.Paused, machine.Mode);
            Assert.IsFalse(machine.MovieTickGateOpen);
            Assert.IsNull(machine.ActiveRequest);
            Assert.IsFalse(machine.InterruptStepAtCompletedBoundary().Success);
            Assert.IsTrue(machine.RequestStep(new StepRequest(StepBoundary.MovieTick, 1)).Success);
            Assert.AreEqual(0, machine.CommittedMovieTicks);
        }

        [TestMethod]
        public void InterruptCannotCaptureUncontrolledRunningGame()
        {
            var machine = new SimulationControlStateMachine();
            Assert.IsFalse(machine.InterruptStepAtCompletedBoundary().Success);
            Assert.AreEqual(SimulationControlMode.Running, machine.Mode);
        }

        [TestMethod]
        public void VisualUpdateStep_TracksItsOwnBoundaryWithoutOpeningMovieGate()
        {
            var machine = Pause();
            var request = new StepRequest(StepBoundary.VisualUpdate, 1);

            Assert.IsTrue(machine.RequestStep(request).Success);
            Assert.IsFalse(machine.MovieTickGateOpen);
            Assert.IsFalse(machine.CommitMovieTick().Success);
            Assert.IsTrue(machine.CommitVisualUpdate().Success);
            Assert.IsTrue(machine.StepQuotaSatisfied);
            Assert.IsTrue(
                machine.CompleteStep(
                    new StepResult(
                        request,
                        0,
                        1,
                        0,
                        1,
                        Array.Empty<TickLedgerRecord>()))
                    .Success);
        }

        [TestMethod]
        public void IllegalTransitions_AreFailClosedAndDoNotChangeMode()
        {
            var machine = new SimulationControlStateMachine();

            Assert.IsFalse(
                machine.RequestStep(
                    new StepRequest(StepBoundary.MovieTick, 1))
                    .Success);
            Assert.IsFalse(machine.CompletePause().Success);
            Assert.IsFalse(machine.RequestRestore().Success);
            Assert.AreEqual(SimulationControlMode.Running, machine.Mode);

            Assert.IsTrue(machine.RequestPause().Success);
            Assert.IsFalse(machine.RequestPause().Success);
            Assert.AreEqual(SimulationControlMode.Pausing, machine.Mode);
        }

        [TestMethod]
        public void RestoreAndFault_PreserveExplicitRecoveryState()
        {
            var machine = Pause();
            Assert.IsTrue(machine.RequestRestore().Success);
            Assert.AreEqual(SimulationControlMode.Restoring, machine.Mode);
            Assert.IsTrue(machine.CompleteRestore().Success);
            Assert.AreEqual(SimulationControlMode.Running, machine.Mode);

            machine.Fault("injected");
            Assert.AreEqual(SimulationControlMode.Faulted, machine.Mode);
            Assert.AreEqual("injected", machine.FaultMessage);
            Assert.IsFalse(machine.MovieTickGateOpen);
            Assert.IsTrue(machine.RequestRestore().Success);
            Assert.IsTrue(machine.CompleteRestore().Success);
            Assert.AreEqual(SimulationControlMode.Running, machine.Mode);
            Assert.AreEqual(string.Empty, machine.FaultMessage);
        }

        [TestMethod]
        public void StepResult_CopiesLedgerAndRejectsInvalidDeltas()
        {
            var records = new List<TickLedgerRecord> { Record(1) };
            var result = new StepResult(
                new StepRequest(StepBoundary.MovieTick, 1),
                1,
                1,
                0,
                1,
                records);
            records.Clear();

            Assert.HasCount(1, result.Ledger);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => new StepResult(
                    new StepRequest(StepBoundary.MovieTick, 1),
                    -1,
                    0,
                    0,
                    0,
                    Array.Empty<TickLedgerRecord>()));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => new StepRequest(StepBoundary.MovieTick, 0));
        }

        [TestMethod]
        public void CompleteStep_RejectsWrongRequestOrReportedDelta()
        {
            var machine = Pause();
            var request = new StepRequest(StepBoundary.MovieTick, 1);
            machine.RequestStep(request);
            machine.CommitMovieTick();

            Assert.IsFalse(
                machine.CompleteStep(
                    new StepResult(
                        new StepRequest(StepBoundary.MovieTick, 2),
                        1,
                        1,
                        0,
                        1,
                        Array.Empty<TickLedgerRecord>()))
                    .Success);
            Assert.IsFalse(
                machine.CompleteStep(
                    new StepResult(
                        request,
                        0,
                        1,
                        0,
                        1,
                        Array.Empty<TickLedgerRecord>()))
                    .Success);
            Assert.AreEqual(SimulationControlMode.Stepping, machine.Mode);
        }

        [TestMethod]
        public void PlaybackRawInputSuspension_AllowsOnlyDeclaredForwardGap()
        {
            var machine = new PlaybackStateMachine();
            Assert.IsTrue(
                machine.StartReplay(
                    Movie(),
                    new PlaybackContext(
                        Manifest,
                        "slot-2-gg-vengefly",
                        Baseline,
                        0))
                    .Success);

            Assert.IsTrue(machine.Advance(Stamp(100)).Success);
            Assert.IsTrue(machine.DeclareRawInputSuspension());
            Assert.IsTrue(machine.Advance(Stamp(125)).Success);
            var undeclaredGap = machine.Advance(Stamp(127));
            Assert.IsFalse(undeclaredGap.Success);
            Assert.AreEqual(PlaybackMode.Stopping, machine.Mode);
        }

        [TestMethod]
        public void PlaybackRawInputSuspension_StillRejectsBackwardTick()
        {
            var machine = new PlaybackStateMachine();
            machine.StartReplay(
                Movie(),
                new PlaybackContext(
                    Manifest,
                    "slot-2-gg-vengefly",
                    Baseline,
                    0));
            Assert.IsTrue(machine.Advance(Stamp(100)).Success);
            Assert.IsTrue(machine.DeclareRawInputSuspension());
            Assert.IsFalse(machine.Advance(Stamp(99)).Success);
        }

        [TestMethod]
        public void RestorePlayback_AllowsOnlyMonotonicSceneEpochAdvance()
        {
            var machine = new PlaybackStateMachine();
            Assert.IsTrue(
                machine.StartReplay(
                    Movie(),
                    new PlaybackContext(
                        Manifest,
                        "slot-2-gg-vengefly",
                        Baseline,
                        3,
                        allowSceneTransitions: true))
                    .Success);

            Assert.IsTrue(machine.Advance(Stamp(100, 3)).Success);
            Assert.IsTrue(machine.Advance(Stamp(101, 5)).Success);
            Assert.IsFalse(machine.Advance(Stamp(102, 4)).Success);
            Assert.AreEqual(PlaybackStopReason.SceneChanged, machine.StopReason);
        }

        private static SimulationControlStateMachine Pause()
        {
            var machine = new SimulationControlStateMachine();
            Assert.IsTrue(machine.RequestPause().Success);
            Assert.IsTrue(machine.CompletePause().Success);
            return machine;
        }

        private static TickLedgerRecord Record(long sequence)
        {
            return new TickLedgerRecord(
                sequence,
                "session",
                Manifest,
                "run",
                "STEP",
                new TickStamp(
                    (ulong)sequence,
                    sequence,
                    sequence,
                    0,
                    TickPhase.InControlCommitted),
                -1,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                "scene",
                string.Empty);
        }

        private static TickStamp Stamp(ulong inputTick)
        {
            return Stamp(inputTick, 0);
        }

        private static TickStamp Stamp(ulong inputTick, int sceneEpoch)
        {
            return new TickStamp(
                inputTick,
                0,
                0,
                sceneEpoch,
                TickPhase.InControlCommitted);
        }

        private static MovieDocument Movie()
        {
            var source = new MovieSourceSpan("control.hktas", 1, 1, 1);
            return new MovieDocument(
                "control.hktas",
                new MovieHeader(
                    1,
                    "1.5.78.11833",
                    "1.5.78.11833-77",
                    Manifest,
                    "slot-2-gg-vengefly",
                    Baseline,
                    "input"),
                new MovieCommand[]
                {
                    new FrameRunCommand(
                        8,
                        Core.Input.TasAction.None,
                        0,
                        0,
                        false,
                        source)
                });
        }
    }
}
