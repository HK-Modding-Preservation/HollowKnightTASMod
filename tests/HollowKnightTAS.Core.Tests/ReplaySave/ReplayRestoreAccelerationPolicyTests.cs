using System;
using System.Threading;
using HollowKnightTAS.Core.ReplaySave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.ReplaySave
{
    [TestClass]
    public sealed class ReplayRestoreAccelerationPolicyTests
    {
        [TestMethod]
        public void MissingAccelerator_RequiresFullReplayWithoutAttempt()
        {
            var descriptor = ReplaySaveTestFactory.Commit(
                "save-acceleration-missing",
                ReplaySaveReason.Manual).Descriptor;

            var plan = ReplayRestoreAccelerationPolicy.EvaluatePlan(
                null,
                descriptor);
            var attempt = ReplayRestoreAccelerationPolicy.TryRestore(
                null,
                plan,
                CancellationToken.None);

            Assert.AreEqual(
                ReplayRestoreAccelerationStatus.Unavailable,
                plan.Status);
            Assert.IsFalse(attempt.Attempted);
            Assert.IsTrue(attempt.RequiresFullReplay);
        }

        [TestMethod]
        public void UnavailableAndIncompatiblePlans_DoNotInvokeRestore()
        {
            foreach (var status in new[]
                     {
                         ReplayRestoreAccelerationStatus.Unavailable,
                         ReplayRestoreAccelerationStatus.Incompatible
                     })
            {
                var accelerator = new FakeAccelerator(
                    new ReplayRestoreAccelerationPlan(
                        status,
                        0,
                        status.ToString()),
                    new ReplayRestoreAccelerationResult(
                        true,
                        8,
                        "must not run"));
                var descriptor = ReplaySaveTestFactory.Commit(
                    "save-acceleration-" + status.ToString().ToLowerInvariant(),
                    ReplaySaveReason.Manual).Descriptor;

                var plan = ReplayRestoreAccelerationPolicy.EvaluatePlan(
                    accelerator,
                    descriptor);
                var attempt = ReplayRestoreAccelerationPolicy.TryRestore(
                    accelerator,
                    plan,
                    CancellationToken.None);

                Assert.AreEqual(status, plan.Status);
                Assert.IsFalse(attempt.Attempted);
                Assert.AreEqual(0, accelerator.RestoreCalls);
                Assert.IsTrue(attempt.RequiresFullReplay);
            }
        }

        [TestMethod]
        public void PlanningFaultAndNullPlan_BecomeFaultedFallbackPlans()
        {
            var descriptor = ReplaySaveTestFactory.Commit(
                "save-acceleration-plan-fault",
                ReplaySaveReason.Manual).Descriptor;
            var throwing = new FakeAccelerator(
                new InvalidOperationException("plan exploded"));
            var nullPlan = new FakeAccelerator(plan: null, result: null);

            var thrown = ReplayRestoreAccelerationPolicy.EvaluatePlan(
                throwing,
                descriptor);
            var missing = ReplayRestoreAccelerationPolicy.EvaluatePlan(
                nullPlan,
                descriptor);

            Assert.AreEqual(
                ReplayRestoreAccelerationStatus.Faulted,
                thrown.Status);
            StringAssert.Contains(thrown.Detail, "plan exploded");
            Assert.AreEqual(
                ReplayRestoreAccelerationStatus.Faulted,
                missing.Status);
            StringAssert.Contains(missing.Detail, "null plan");
        }

        [TestMethod]
        public void RestoreFaultNullAndDecline_AllRequireFullReplay()
        {
            var ready = new ReplayRestoreAccelerationPlan(
                ReplayRestoreAccelerationStatus.Ready,
                8,
                "ready");
            var accelerators = new[]
            {
                new FakeAccelerator(
                    ready,
                    new InvalidOperationException("restore exploded")),
                new FakeAccelerator(ready, result: null),
                new FakeAccelerator(
                    ready,
                    new ReplayRestoreAccelerationResult(
                        false,
                        0,
                        "declined"))
            };

            var fault = ReplayRestoreAccelerationPolicy.TryRestore(
                accelerators[0],
                ready,
                CancellationToken.None);
            var missing = ReplayRestoreAccelerationPolicy.TryRestore(
                accelerators[1],
                ready,
                CancellationToken.None);
            var decline = ReplayRestoreAccelerationPolicy.TryRestore(
                accelerators[2],
                ready,
                CancellationToken.None);

            Assert.IsTrue(fault.Attempted);
            Assert.IsTrue(fault.Faulted);
            Assert.IsTrue(fault.RequiresFullReplay);
            StringAssert.Contains(fault.Detail, "restore exploded");
            Assert.IsTrue(missing.Faulted);
            StringAssert.Contains(missing.Detail, "null result");
            Assert.IsFalse(decline.Faulted);
            Assert.IsTrue(decline.RequiresFullReplay);
            Assert.AreEqual(3, accelerators.Length);
        }

        [TestMethod]
        public void ReadySuccess_PreservesResumeCursorForRuntimeVerification()
        {
            var descriptor = ReplaySaveTestFactory.Commit(
                "save-acceleration-ready",
                ReplaySaveReason.Manual).Descriptor;
            var ready = new ReplayRestoreAccelerationPlan(
                ReplayRestoreAccelerationStatus.Ready,
                8,
                "ready");
            var accelerator = new FakeAccelerator(
                ready,
                new ReplayRestoreAccelerationResult(
                    true,
                    8,
                    "restored"));

            var plan = ReplayRestoreAccelerationPolicy.EvaluatePlan(
                accelerator,
                descriptor);
            var attempt = ReplayRestoreAccelerationPolicy.TryRestore(
                accelerator,
                plan,
                CancellationToken.None);

            Assert.AreEqual(
                ReplayRestoreAccelerationStatus.Ready,
                plan.Status);
            Assert.IsTrue(attempt.Attempted);
            Assert.IsTrue(attempt.Success);
            Assert.IsFalse(attempt.Faulted);
            Assert.IsFalse(attempt.RequiresFullReplay);
            Assert.AreEqual(8, attempt.ResumeMovieTick);
            Assert.AreEqual(1, accelerator.RestoreCalls);
        }

        private sealed class FakeAccelerator : IReplayRestoreAccelerator
        {
            private readonly ReplayRestoreAccelerationPlan? plan;
            private readonly ReplayRestoreAccelerationResult? result;
            private readonly Exception? planException;
            private readonly Exception? restoreException;

            public FakeAccelerator(
                ReplayRestoreAccelerationPlan? plan,
                ReplayRestoreAccelerationResult? result)
            {
                this.plan = plan;
                this.result = result;
            }

            public FakeAccelerator(Exception planException)
            {
                this.planException = planException;
            }

            public FakeAccelerator(
                ReplayRestoreAccelerationPlan plan,
                Exception restoreException)
            {
                this.plan = plan;
                this.restoreException = restoreException;
            }

            public int RestoreCalls { get; private set; }

            public ReplayRestoreAccelerationPlan TryPlan(
                ReplaySaveDescriptor save)
            {
                if (planException != null)
                {
                    throw planException;
                }

                return plan!;
            }

            public ReplayRestoreAccelerationResult TryRestore(
                ReplayRestoreAccelerationPlan value,
                CancellationToken cancellationToken)
            {
                RestoreCalls++;
                if (restoreException != null)
                {
                    throw restoreException;
                }

                return result!;
            }
        }
    }
}
