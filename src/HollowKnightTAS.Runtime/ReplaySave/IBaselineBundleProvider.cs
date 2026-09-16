using System;
using HollowKnightTAS.Core.ReplaySave;
using HollowKnightTAS.Runtime.State;

namespace HollowKnightTAS.Runtime.ReplaySave
{
    public sealed class BaselineCaptureResult
    {
        private BaselineCaptureResult(
            BaselineBundle? bundle,
            byte[]? canonicalBytes,
            string error)
        {
            Bundle = bundle;
            CanonicalBytes = canonicalBytes == null
                ? null
                : (byte[])canonicalBytes.Clone();
            Error = error ?? string.Empty;
        }

        public bool Success => Bundle != null && CanonicalBytes != null;
        public BaselineBundle? Bundle { get; }
        public byte[]? CanonicalBytes { get; }
        public string Error { get; }

        public static BaselineCaptureResult Succeeded(
            BaselineBundle bundle,
            byte[] canonicalBytes)
        {
            return new BaselineCaptureResult(
                bundle ?? throw new ArgumentNullException(nameof(bundle)),
                canonicalBytes
                ?? throw new ArgumentNullException(nameof(canonicalBytes)),
                string.Empty);
        }

        public static BaselineCaptureResult Failed(string error)
        {
            return new BaselineCaptureResult(
                null,
                null,
                string.IsNullOrWhiteSpace(error)
                    ? "Baseline capture failed."
                    : error);
        }
    }

    public enum UserOverwriteApproval : byte
    {
        None = 0,
        Approved = 1,
        Denied = 2
    }

    public interface IBaselineBundleProvider
    {
        BaselineCaptureResult CaptureCurrentBaseline(
            string baselineId,
            int saveSlot,
            SnapshotCaptureResult semanticCapture,
            DateTimeOffset capturedAtUtc);

        BaselineInstallPlan PlanInstall(
            BaselineBundle bundle,
            int dedicatedTasSlot);

        BaselineInstallResult Install(
            BaselineInstallPlan plan,
            UserOverwriteApproval approval);
    }
}
