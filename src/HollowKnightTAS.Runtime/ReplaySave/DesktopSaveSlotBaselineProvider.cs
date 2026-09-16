using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.ReplaySave;
using HollowKnightTAS.Core.Serialization;
using HollowKnightTAS.Runtime.State;
using UnityEngine;

namespace HollowKnightTAS.Runtime.ReplaySave
{
    public sealed class BaselineInstallPlan
    {
        internal BaselineInstallPlan(
            BaselineBundle bundle,
            int dedicatedTasSlot,
            string savePath,
            string moddedSavePath,
            byte[]? currentSaveData,
            byte[]? currentModdedSaveData,
            byte[] targetSaveData,
            byte[]? targetModdedSaveData)
        {
            Bundle = bundle;
            DedicatedTasSlot = dedicatedTasSlot;
            SavePath = savePath;
            ModdedSavePath = moddedSavePath;
            CurrentSaveData = currentSaveData == null
                ? null
                : (byte[])currentSaveData.Clone();
            CurrentModdedSaveData = currentModdedSaveData == null
                ? null
                : (byte[])currentModdedSaveData.Clone();

            TargetSaveData = (byte[])targetSaveData.Clone();
            TargetModdedSaveData = targetModdedSaveData == null ? null : (byte[])targetModdedSaveData.Clone();
            IsAlreadyInstalled =
                BytesEqual(CurrentSaveData, TargetSaveData)
                && BytesEqual(CurrentModdedSaveData, TargetModdedSaveData);
            RequiresOverwriteApproval =
                !IsAlreadyInstalled
                && (CurrentSaveData != null || CurrentModdedSaveData != null);
        }

        public BaselineBundle Bundle { get; }
        public int DedicatedTasSlot { get; }
        public bool IsAlreadyInstalled { get; }
        public bool RequiresOverwriteApproval { get; }
        internal string SavePath { get; }
        internal string ModdedSavePath { get; }
        internal byte[]? CurrentSaveData { get; }
        internal byte[]? CurrentModdedSaveData { get; }
        internal byte[] TargetSaveData { get; }
        internal byte[]? TargetModdedSaveData { get; }

        internal static bool BytesEqual(byte[]? left, byte[]? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }

            return true;
        }
    }

    public sealed class BaselineInstallResult
    {
        public BaselineInstallResult(
            bool success,
            bool wroteSlot,
            bool cancelled,
            string backupDirectory,
            string error)
        {
            Success = success;
            WroteSlot = wroteSlot;
            Cancelled = cancelled;
            BackupDirectory = backupDirectory ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public bool Success { get; }
        public bool WroteSlot { get; }
        public bool Cancelled { get; }
        public string BackupDirectory { get; }
        public string Error { get; }
    }

    public sealed class DesktopSaveSlotBaselineProvider :
        IBaselineBundleProvider
    {
        private readonly string persistentDataPath;
        private readonly string backupRoot;

        public DesktopSaveSlotBaselineProvider()
            : this(
                Application.persistentDataPath,
                Path.Combine(
                    Application.persistentDataPath,
                    "HollowKnightTAS",
                    "replay-saves",
                    "v1",
                    "slot-backups"))
        {
        }

        public DesktopSaveSlotBaselineProvider(
            string persistentDataPath,
            string backupRoot)
        {
            if (string.IsNullOrWhiteSpace(persistentDataPath)
                || string.IsNullOrWhiteSpace(backupRoot))
            {
                throw new ArgumentException(
                    "Persistent-data and backup roots are required.");
            }

            this.persistentDataPath = Path.GetFullPath(persistentDataPath);
            this.backupRoot = Path.GetFullPath(backupRoot);
            Directory.CreateDirectory(this.backupRoot);
        }

        public BaselineCaptureResult CaptureCurrentBaseline(
            string baselineId,
            int saveSlot,
            SnapshotCaptureResult semanticCapture,
            DateTimeOffset capturedAtUtc)
        {
            if (semanticCapture == null)
            {
                throw new ArgumentNullException(nameof(semanticCapture));
            }

            if (!semanticCapture.Success
                || semanticCapture.CanonicalBytes == null
                || semanticCapture.Sha256 == null)
            {
                return BaselineCaptureResult.Failed(
                    "A successful semantic baseline capture is required.");
            }

            try
            {
                var savePath = SavePath(saveSlot);
                if (!File.Exists(savePath))
                {
                    return BaselineCaptureResult.Failed(
                        "Desktop save slot bytes are not available at "
                        + Path.GetFileName(savePath)
                        + ".");
                }

                var moddedPath = ModdedSavePath(saveSlot);
                var bundle = new BaselineBundle(
                    BaselineBundle.CurrentSchemaVersion,
                    baselineId,
                    semanticCapture.Sha256,
                    saveSlot,
                    capturedAtUtc,
                    ReadBounded(
                        savePath,
                        BaselineBundle.MaximumSaveBytes),
                    File.Exists(moddedPath)
                        ? ReadBounded(
                            moddedPath,
                            BaselineBundle.MaximumModdedSaveBytes,
                            allowEmpty: true)
                        : null,
                    semanticCapture.CanonicalBytes);
                return BaselineCaptureResult.Succeeded(
                    bundle,
                    BaselineBundleCodec.Serialize(bundle));
            }
            catch (Exception exception)
            {
                return BaselineCaptureResult.Failed(
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        public BaselineInstallPlan PlanInstall(
            BaselineBundle bundle,
            int dedicatedTasSlot)
        {
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));
            return PlanFiles(bundle, dedicatedTasSlot, bundle.SaveData, bundle.ModdedSaveData);
        }

        internal BaselineInstallPlan PlanLifecycleInstall(BaselineBundle root,
            ReplayLifecycleRecord operation, IReadOnlyDictionary<string, byte[]> objects)
        {
            if (operation.Kind != ReplayLifecycleKind.LoadSlot || operation.ModdedSlotObjectSha256 == null)
                throw new InvalidDataException("Lifecycle does not contain a complete slot capture.");
            var save = ReadObject(operation.SlotObjectSha256, false);
            var modded = operation.ModdedSlotObjectSha256.Length == 0 ? null
                : ReadObject(operation.ModdedSlotObjectSha256, true);
            // Root is provenance only; the lifecycle's files are not presented
            // as a new semantic baseline and do not reset the recording clock.
            return PlanFiles(root, operation.Slot, save, modded);

            byte[] ReadObject(string hash, bool allowEmpty)
            {
                if (!objects.TryGetValue(hash, out var bytes) || bytes == null)
                    throw new InvalidDataException("Lifecycle slot object is missing.");
                var copy = (byte[])bytes.Clone();
                if ((!allowEmpty && copy.Length == 0) || copy.Length > ReplayLifecycleLog.MaximumSlotBytes
                    || Sha256Utility.ComputeHex(copy) != hash)
                    throw new InvalidDataException("Lifecycle slot object is invalid.");
                return copy;
            }
        }

        private BaselineInstallPlan PlanFiles(BaselineBundle bundle, int dedicatedTasSlot,
            byte[] targetSave, byte[]? targetModded)
        {
            if (bundle == null)
            {
                throw new ArgumentNullException(nameof(bundle));
            }

            var savePath = SavePath(dedicatedTasSlot);
            var moddedPath = ModdedSavePath(dedicatedTasSlot);
            return new BaselineInstallPlan(
                bundle,
                dedicatedTasSlot,
                savePath,
                moddedPath,
                File.Exists(savePath)
                    ? ReadBounded(
                        savePath,
                        BaselineBundle.MaximumSaveBytes)
                    : null,
                File.Exists(moddedPath)
                    ? ReadBounded(
                        moddedPath,
                        BaselineBundle.MaximumModdedSaveBytes,
                        allowEmpty: true)
                    : null,
                targetSave, targetModded);
        }

        public BaselineInstallResult Install(
            BaselineInstallPlan plan,
            UserOverwriteApproval approval)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            // Approval is for the bytes observed when the plan was presented,
            // never for arbitrary later contents of the same numbered slot.
            var current = PlanFiles(plan.Bundle, plan.DedicatedTasSlot, plan.TargetSaveData, plan.TargetModdedSaveData);
            if (!BaselineInstallPlan.BytesEqual(plan.CurrentSaveData, current.CurrentSaveData)
                || !BaselineInstallPlan.BytesEqual(plan.CurrentModdedSaveData, current.CurrentModdedSaveData))
            {
                return new BaselineInstallResult(false, false, false, string.Empty,
                    "TAS slot changed after planning; explicit approval must be requested again. No files were written.");
            }

            if (plan.IsAlreadyInstalled)
            {
                return new BaselineInstallResult(
                    true,
                    false,
                    false,
                    string.Empty,
                    string.Empty);
            }

            if (plan.RequiresOverwriteApproval
                && approval != UserOverwriteApproval.Approved)
            {
                return new BaselineInstallResult(
                    false,
                    false,
                    approval == UserOverwriteApproval.Denied,
                    string.Empty,
                    approval == UserOverwriteApproval.Denied
                        ? "User cancelled TAS slot overwrite."
                        : "Explicit TAS slot overwrite approval is required.");
            }

            var backupDirectory = string.Empty;
            try
            {
                if (plan.CurrentSaveData != null
                    || plan.CurrentModdedSaveData != null)
                {
                    backupDirectory = CreateBackup(plan);
                }

                WriteAtomic(plan.SavePath, plan.TargetSaveData);
                var targetModded = plan.TargetModdedSaveData;
                if (targetModded == null)
                {
                    if (File.Exists(plan.ModdedSavePath))
                    {
                        File.Delete(plan.ModdedSavePath);
                    }
                }
                else
                {
                    WriteAtomic(plan.ModdedSavePath, targetModded);
                }

                return new BaselineInstallResult(
                    true,
                    true,
                    false,
                    backupDirectory,
                    string.Empty);
            }
            catch (Exception exception)
            {
                try
                {
                    RestoreOriginal(plan);
                }
                catch (Exception rollback)
                {
                    return new BaselineInstallResult(
                        false,
                        false,
                        false,
                        backupDirectory,
                        exception.GetType().Name
                        + ": "
                        + exception.Message
                        + "; rollback="
                        + rollback.GetType().Name
                        + ": "
                        + rollback.Message);
                }

                return new BaselineInstallResult(
                    false,
                    false,
                    false,
                    backupDirectory,
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal void RestoreInstalled(BaselineInstallPlan plan)
        {
            var current = PlanFiles(plan.Bundle, plan.DedicatedTasSlot, plan.TargetSaveData, plan.TargetModdedSaveData);
            if (!SlotRollbackGuard.CanRestore(current.CurrentSaveData, plan.CurrentSaveData, plan.TargetSaveData)
                || !SlotRollbackGuard.CanRestore(current.CurrentModdedSaveData, plan.CurrentModdedSaveData, plan.TargetModdedSaveData))
                throw new InvalidOperationException("Slot changed after installation; preserving current files and the original backup instead of overwriting them during rollback.");
            // Validate both files before writing either. An interrupted rollback
            // is retryable, but unrelated user changes are never overwritten.
            if (!BaselineInstallPlan.BytesEqual(current.CurrentSaveData, plan.CurrentSaveData))
                RestoreFile(plan.SavePath, plan.CurrentSaveData);
            if (!BaselineInstallPlan.BytesEqual(current.CurrentModdedSaveData, plan.CurrentModdedSaveData))
                RestoreFile(plan.ModdedSavePath, plan.CurrentModdedSaveData);
        }

        internal void RestoreOriginal(BaselineInstallPlan plan)
        {
            RestoreFile(plan.SavePath, plan.CurrentSaveData);
            RestoreFile(plan.ModdedSavePath, plan.CurrentModdedSaveData);
        }

        private string CreateBackup(BaselineInstallPlan plan)
        {
            var directory = Path.Combine(
                backupRoot,
                DateTimeOffset.UtcNow.ToString(
                    "yyyyMMdd'T'HHmmss.fffffff'Z'",
                    CultureInfo.InvariantCulture)
                + "-slot-"
                + plan.DedicatedTasSlot.ToString(
                    CultureInfo.InvariantCulture)
                + "-"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            if (plan.CurrentSaveData != null)
            {
                WriteAtomic(
                    Path.Combine(directory, "original.dat"),
                    plan.CurrentSaveData);
            }

            if (plan.CurrentModdedSaveData != null)
            {
                WriteAtomic(
                    Path.Combine(directory, "original.modded.json"),
                    plan.CurrentModdedSaveData);
            }

            var builder = new StringBuilder(512);
            builder.Append("{\"moddedSaveSha256\":");
            if (plan.CurrentModdedSaveData == null)
            {
                builder.Append("null");
            }
            else
            {
                CanonicalJsonWriter.AppendString(
                    builder,
                    Sha256Utility.ComputeHex(plan.CurrentModdedSaveData));
            }

            builder.Append(",\"saveSha256\":");
            if (plan.CurrentSaveData == null)
            {
                builder.Append("null");
            }
            else
            {
                CanonicalJsonWriter.AppendString(
                    builder,
                    Sha256Utility.ComputeHex(plan.CurrentSaveData));
            }

            builder.Append(",\"slot\":");
            builder.Append(
                plan.DedicatedTasSlot.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append('}');
            WriteAtomic(
                Path.Combine(directory, "manifest.json"),
                new UTF8Encoding(false).GetBytes(builder.ToString()));
            return directory;
        }

        private string SavePath(int slot)
        {
            ValidateSlot(slot);
            return Path.Combine(
                persistentDataPath,
                "user"
                + slot.ToString(CultureInfo.InvariantCulture)
                + ".dat");
        }

        private string ModdedSavePath(int slot)
        {
            ValidateSlot(slot);
            return Path.Combine(
                persistentDataPath,
                "user"
                + slot.ToString(CultureInfo.InvariantCulture)
                + ".modded.json");
        }

        private static void ValidateSlot(int slot)
        {
            if (slot <= 0 || slot > 4)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(slot),
                    "Hollow Knight desktop slots are 1 through 4.");
            }
        }

        private static byte[] ReadBounded(
            string path,
            int maximumBytes,
            bool allowEmpty = false)
        {
            var info = new FileInfo(path);
            if ((!allowEmpty && info.Length <= 0)
                || info.Length > maximumBytes)
            {
                throw new InvalidDataException(
                    "Save file size is outside the supported range.");
            }

            return File.ReadAllBytes(path);
        }

        private static void RestoreFile(string path, byte[]? bytes)
        {
            if (bytes == null)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }

            WriteAtomic(path, bytes);
        }

        private static void WriteAtomic(string destinationPath, byte[] bytes)
        {
            var temporaryPath =
                destinationPath
                + ".tmp-"
                + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(destinationPath))
                {
                    File.Replace(temporaryPath, destinationPath, null);
                }
                else
                {
                    File.Move(temporaryPath, destinationPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }
}
