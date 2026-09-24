using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.ReplaySave;
using Modding;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HollowKnightTAS.Runtime.ReplaySave
{
    public sealed class ProtectedSaveRedirector : ISavePathResolver, IDisposable
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly FieldInfo SaveDirField = typeof(DesktopPlatform).GetField("saveDirPath", PrivateInstance)
            ?? throw new MissingFieldException("DesktopPlatform.saveDirPath");
        private static readonly FieldInfo OnlineField = typeof(DesktopPlatform).GetField("onlineSubsystem", PrivateInstance)
            ?? throw new MissingFieldException("DesktopPlatform.onlineSubsystem");
        private readonly ProtectedSaveDescriptor descriptor;
        private readonly DesktopSavePathResolver paths;
        private readonly DesktopPlatform desktop;
        private readonly Hook moddedSaveHook;
        private bool disposed;

        public ProtectedSaveDescriptor Descriptor => descriptor;

        private ProtectedSaveRedirector(ProtectedSaveDescriptor descriptor,
            DesktopPlatform desktop, Hook moddedSaveHook)
        {
            this.descriptor = descriptor;
            this.desktop = desktop;
            this.moddedSaveHook = moddedSaveHook;
            paths = new DesktopSavePathResolver(descriptor.ShadowRoot);
        }

        public static ProtectedSaveRedirector Install(ProtectedSaveDescriptor descriptor,
            Action<string> log)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (log == null) throw new ArgumentNullException(nameof(log));
            if (!SavePathResolver.ProtectionRequested)
                throw new InvalidOperationException("Protected save launch intent is missing.");
            var original = Path.GetFullPath(descriptor.OriginalRoot).TrimEnd('\\', '/');
            var actual = Path.GetFullPath(Application.persistentDataPath).TrimEnd('\\', '/');
            if (!string.Equals(original, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Game save root differs from the protected original root.");
            var shadow = Path.GetFullPath(descriptor.ShadowRoot).TrimEnd('\\', '/');
            var expectedParent = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HollowKnightTAS", "save-shadows");
            if (!shadow.StartsWith(expectedParent + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(shadow)
                || (new DirectoryInfo(shadow).Attributes & FileAttributes.ReparsePoint) != 0
                || !string.Equals(Path.GetFileName(shadow), descriptor.RunId,
                    StringComparison.Ordinal))
                throw new InvalidOperationException("Protected shadow save root is invalid.");
            if (!NativeSaveGuard.Confirm(descriptor.OriginalRoot, descriptor.GuardToken))
                throw new InvalidOperationException("Native save guard identity is missing or changed.");
            VerifyShadowFiles(descriptor);
            if (!(Platform.Current is DesktopPlatform desktop))
                throw new InvalidOperationException("Protected saves require DesktopPlatform.");
            var online = OnlineField.GetValue(desktop) as DesktopOnlineSubsystem;
            if (online != null && online.HandlesGameSaves)
                throw new InvalidOperationException("Online save subsystem is unsupported for protected TAS.");

            var method = typeof(GameManager).GetMethod("ModdedSavePath", PrivateStatic,
                null, new[] { typeof(int) }, null)
                ?? throw new MissingMethodException("GameManager.ModdedSavePath(int)");
            Hook? hook = null;
            ProtectedSaveRedirector? redirector = null;
            var priorDir = (string?)SaveDirField.GetValue(desktop);
            try
            {
                SaveDirField.SetValue(desktop, shadow);
                hook = new Hook(method, (Func<Func<int, string>, int, string>)
                    ((_, slot) => SavePathResolver.Current.GetSlotPath(slot, ".modded.json")));
                redirector = new ProtectedSaveRedirector(descriptor, desktop, hook);
                SavePathResolver.SetProtected(redirector);
                On.DesktopPlatform.GetSaveSlotPath += redirector.OnGetSaveSlotPath;
                On.DesktopPlatform.IsSaveSlotInUse += redirector.OnIsSaveSlotInUse;
                On.DesktopPlatform.ReadSaveSlot += redirector.OnReadSaveSlot;
                On.DesktopPlatform.WriteSaveSlot += redirector.OnWriteSaveSlot;
                On.DesktopPlatform.ClearSaveSlot += redirector.OnClearSaveSlot;
                redirector.AssertActive();
                log("Protected save redirector active: " + descriptor.RunId);
                return redirector;
            }
            catch
            {
                redirector?.Dispose();
                hook?.Dispose();
                SaveDirField.SetValue(desktop, priorDir);
                throw;
            }
        }

        public string GetSlotPath(int slot, string suffix)
        {
            AssertActive();
            return paths.GetSlotPath(slot, suffix);
        }

        public string GetTasDataPath(params string[] segments)
        {
            AssertActive();
            return paths.GetTasDataPath(segments);
        }

        public void AssertActive()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ProtectedSaveRedirector));
            if (!NativeSaveGuard.Confirm(descriptor.OriginalRoot, descriptor.GuardToken))
                throw new InvalidOperationException("Native save guard identity changed.");
            if (!ReferenceEquals(Platform.Current, desktop)
                || !string.Equals(SaveDirField.GetValue(desktop) as string,
                    descriptor.ShadowRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Desktop save path left the protected shadow root.");
            var online = OnlineField.GetValue(desktop) as DesktopOnlineSubsystem;
            if (online != null && online.HandlesGameSaves)
                throw new InvalidOperationException("Online saves became active during protected TAS.");
            foreach (var mod in ModHooks.GetAllMods(onlyEnabled: true, allowLoadError: false))
                if (!string.Equals(mod.GetName(), "HollowKnightTAS", StringComparison.Ordinal))
                    throw new InvalidOperationException("An unreviewed Mod is enabled during protected TAS: "
                        + mod.GetName());
        }

        private string OnGetSaveSlotPath(On.DesktopPlatform.orig_GetSaveSlotPath original,
            DesktopPlatform self, int slot, int usage)
        {
            AssertActive();
            var path = original(self, slot, usage);
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)),
                    descriptor.ShadowRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Desktop save path escaped the protected shadow root.");
            return path;
        }

        private void OnIsSaveSlotInUse(On.DesktopPlatform.orig_IsSaveSlotInUse original,
            DesktopPlatform self, int slot, Action<bool> callback)
        {
            AssertActive();
            original(self, slot, callback);
        }

        private void OnReadSaveSlot(On.DesktopPlatform.orig_ReadSaveSlot original,
            DesktopPlatform self, int slot, Action<byte[]> callback)
        {
            AssertActive();
            original(self, slot, callback);
        }

        private void OnWriteSaveSlot(On.DesktopPlatform.orig_WriteSaveSlot original,
            DesktopPlatform self, int slot, byte[] bytes, Action<bool> callback)
        {
            AssertActive();
            original(self, slot, bytes, callback);
        }

        private void OnClearSaveSlot(On.DesktopPlatform.orig_ClearSaveSlot original,
            DesktopPlatform self, int slot, Action<bool> callback)
        {
            AssertActive();
            original(self, slot, callback);
        }

        private static void VerifyShadowFiles(ProtectedSaveDescriptor descriptor)
        {
            foreach (var pair in descriptor.OriginalFileSha256)
            {
                var path = Path.Combine(descriptor.ShadowRoot, pair.Key);
                if (!File.Exists(path) || Sha256Utility.ComputeFileHex(path) != pair.Value)
                    throw new InvalidOperationException("Shadow save copy differs before game input: " + pair.Key);
            }
            foreach (var path in Directory.GetFiles(descriptor.ShadowRoot, "user*", SearchOption.TopDirectoryOnly))
                if (ProtectedSaveDescriptor.IsSlotFileName(Path.GetFileName(path))
                    && !descriptor.OriginalFileSha256.ContainsKey(Path.GetFileName(path)))
                    throw new InvalidOperationException("Unexpected shadow save file before game input.");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            On.DesktopPlatform.GetSaveSlotPath -= OnGetSaveSlotPath;
            On.DesktopPlatform.IsSaveSlotInUse -= OnIsSaveSlotInUse;
            On.DesktopPlatform.ReadSaveSlot -= OnReadSaveSlot;
            On.DesktopPlatform.WriteSaveSlot -= OnWriteSaveSlot;
            On.DesktopPlatform.ClearSaveSlot -= OnClearSaveSlot;
            moddedSaveHook.Dispose();
            SavePathResolver.ClearProtected(this);
        }

        private static class NativeSaveGuard
        {
            [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
            private delegate int GuardStatus();
            [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
            private delegate int GuardIdentity(
                [MarshalAs(UnmanagedType.LPWStr)] string originalRoot,
                [MarshalAs(UnmanagedType.LPWStr)] string token);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr GetModuleHandle(string name);
            [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
            private static extern IntPtr GetProcAddress(IntPtr module, string name);

            internal static bool Confirm(string root, string token)
            {
                var module = GetModuleHandle("HollowKnightTAS.ClockBridge.dll");
                if (module == IntPtr.Zero) return false;
                var status = GetProcAddress(module, "HktasClockBridge_GetSaveGuardInstallStatus");
                var identity = GetProcAddress(module, "HktasClockBridge_ConfirmSaveGuardIdentity");
                if (status == IntPtr.Zero || identity == IntPtr.Zero) return false;
                return Marshal.GetDelegateForFunctionPointer<GuardStatus>(status)() == 1
                    && Marshal.GetDelegateForFunctionPointer<GuardIdentity>(identity)(root, token) == 1;
            }
        }
    }
}
