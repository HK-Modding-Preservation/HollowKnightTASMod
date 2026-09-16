using System;
using HollowKnightTAS.Core.ReplaySave;

namespace HollowKnightTAS.Runtime.ReplaySave
{
    public sealed class DedicatedTasSlotLease : IDisposable
    {
        private readonly DesktopSaveSlotBaselineProvider provider;
        private bool installed;
        private bool committed;
        private bool disposed;
        private SlotRecoveryStore? recoveryStore;
        private SlotRecoveryRecord? recoveryRecord;
        private string? recoveryHash;

        public DedicatedTasSlotLease(
            DesktopSaveSlotBaselineProvider provider,
            BaselineInstallPlan plan)
        {
            this.provider = provider
                            ?? throw new ArgumentNullException(nameof(provider));
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        }

        public BaselineInstallPlan Plan { get; }
        public BaselineInstallResult? InstallResult { get; private set; }

        public void ConfigureRecovery(SlotRecoveryStore store, string operationId,
            int processId, DateTimeOffset processStartedUtc, int sequence)
        {
            if (disposed || InstallResult != null || recoveryRecord != null)
                throw new InvalidOperationException("Recovery must be configured once before installation.");
            recoveryStore = store ?? throw new ArgumentNullException(nameof(store));
            recoveryRecord = new SlotRecoveryRecord(operationId, processId, processStartedUtc, sequence,
                Plan.DedicatedTasSlot, Plan.CurrentSaveData, Plan.CurrentModdedSaveData,
                Plan.TargetSaveData, Plan.TargetModdedSaveData);
        }

        public BaselineInstallResult Install(UserOverwriteApproval approval)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(DedicatedTasSlotLease));
            }

            if (installed)
            {
                return InstallResult!;
            }

            if (!Plan.IsAlreadyInstalled && (!Plan.RequiresOverwriteApproval || approval == UserOverwriteApproval.Approved)
                && recoveryRecord != null)
                recoveryHash = recoveryStore!.Publish(recoveryRecord);
            InstallResult = provider.Install(Plan, approval);
            installed = InstallResult.Success && InstallResult.WroteSlot;
            return InstallResult;
        }

        public void Commit()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(DedicatedTasSlotLease));
            }

            FinishRecovery(true);
            committed = true;
        }

        public void Rollback()
        {
            if (disposed || !installed || committed)
            {
                return;
            }

            provider.RestoreInstalled(Plan);
            FinishRecovery(false);
            installed = false;
        }

        private void FinishRecovery(bool commit)
        {
            if (recoveryHash != null)
                recoveryStore!.MarkComplete(recoveryRecord!.OperationId, recoveryHash, commit);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            try
            {
                Rollback();
            }
            finally
            {
                disposed = true;
            }
        }
    }
}
