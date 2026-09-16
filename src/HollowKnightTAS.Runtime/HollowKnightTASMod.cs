using System;
using System.Collections.Generic;
using HollowKnightTAS.Core.Automation;
using HollowKnightTAS.Runtime.Runtime;
using HollowKnightTAS.Runtime.Settings;
using Modding;
using UnityEngine;

namespace HollowKnightTAS.Runtime
{
    public sealed class HollowKnightTASMod :
        Mod,
        IGlobalSettings<TasGlobalSettings>,
        IMenuMod
    {
        public const string Version = "0.1.0";

        private TasGlobalSettings? settings;
        private TasRuntimeHost? runtimeHost;
        private bool hooksRegistered;

        public HollowKnightTASMod()
            : base("HollowKnightTAS")
        {
            settings ??= new TasGlobalSettings();
            settings.Normalize();
        }

        public override string GetVersion()
        {
            return Version;
        }

        public override void Initialize(
            Dictionary<string, Dictionary<string, GameObject>> preloadedObjects)
        {
            if (hooksRegistered)
            {
                LogWarn("T01 Initialize called more than once; duplicate registration was ignored.");
                return;
            }

            hooksRegistered = true;
            runtimeHost = new TasRuntimeHost(
                settings ?? new TasGlobalSettings(),
                message => Log(message),
                message => LogDebug(message),
                message => LogWarn(message),
                message => LogError(message));

            ModHooks.FinishedLoadingModsHook += OnFinishedLoadingMods;
            ModHooks.ApplicationQuitHook += OnApplicationQuit;
            Log("T01 hooks registered.");
        }

        public void OnLoadGlobal(TasGlobalSettings loadedSettings)
        {
            settings = loadedSettings ?? new TasGlobalSettings();
            settings.Normalize();
        }

        public TasGlobalSettings OnSaveGlobal()
        {
            settings ??= new TasGlobalSettings();
            settings.Normalize();
            return settings;
        }

        public bool ToggleButtonInsideMenu => false;

        public List<IMenuMod.MenuEntry> GetMenuData(
            IMenuMod.MenuEntry? toggleButtonEntry)
        {
            settings ??= new TasGlobalSettings();
            settings.Normalize();
            return new List<IMenuMod.MenuEntry>
            {
                new IMenuMod.MenuEntry(
                    "External automation",
                    new[]
                    {
                        "Disabled",
                        "Read-only",
                        "Approved control"
                    },
                    "Local non-visual SDK/CLI/MCP access. "
                    + "Approved control enables leased typed commands. "
                    + "Restart Hollow Knight after changing this setting.",
                    SaveAutomationMode,
                    LoadAutomationMode),
                new IMenuMod.MenuEntry(
                    "Debug state mutation",
                    new[] { "Disabled", "Enabled" },
                    "Experimental pose/resource writers. Requires "
                    + "Approved control and restart; any successful "
                    + "mutation permanently makes that process "
                    + "ineligible for verification evidence.",
                    SaveDebugMutation,
                    () => settings.DebugMutationEnabled ? 1 : 0)
            };
        }

        private void SaveAutomationMode(int valueIndex)
        {
            settings ??= new TasGlobalSettings();
            switch (valueIndex)
            {
                case 0:
                    settings.ExternalAutomationMode =
                        nameof(AutomationMode.Disabled);
                    break;
                case 1:
                    settings.ExternalAutomationMode =
                        nameof(AutomationMode.ReadOnly);
                    break;
                case 2:
                    settings.ExternalAutomationMode =
                        nameof(AutomationMode.ApprovedControl);
                    LogWarn(
                        "T15 ApprovedControl selected by the user; "
                        + "a Hollow Knight restart is required.");
                    break;
                default:
                    settings.ExternalAutomationMode =
                        nameof(AutomationMode.ReadOnly);
                    LogWarn(
                        "T15 invalid automation menu value failed "
                        + "closed to ReadOnly.");
                    break;
            }

            settings.Normalize();
            Log(
                "T15 external automation setting changed to "
                + settings.ExternalAutomationMode
                + "; takes effect after restart.");
        }

        private int LoadAutomationMode()
        {
            settings ??= new TasGlobalSettings();
            switch (AutomationModeCodec.Normalize(
                        settings.ExternalAutomationMode))
            {
                case AutomationMode.Disabled:
                    return 0;
                case AutomationMode.ApprovedControl:
                    return 2;
                default:
                    return 1;
            }
        }

        private void SaveDebugMutation(int valueIndex)
        {
            settings ??= new TasGlobalSettings();
            settings.DebugMutationEnabled = valueIndex == 1;
            LogWarn(
                "T15 DebugMutationEnabled changed to "
                + (settings.DebugMutationEnabled
                    ? "true"
                    : "false")
                + " by the user; a Hollow Knight restart is required.");
        }

        private void OnFinishedLoadingMods()
        {
            try
            {
                runtimeHost?.Start();
            }
            catch (Exception exception)
            {
                LogError("T01 runtime host failed to start: " + exception);
            }
        }

        private void OnApplicationQuit()
        {
            try
            {
                runtimeHost?.Stop("application-quit");
            }
            finally
            {
                UnregisterHooks();
            }
        }

        private void UnregisterHooks()
        {
            if (!hooksRegistered)
            {
                return;
            }

            hooksRegistered = false;
            ModHooks.FinishedLoadingModsHook -= OnFinishedLoadingMods;
            ModHooks.ApplicationQuitHook -= OnApplicationQuit;
            runtimeHost = null;
            LogDebug("T01 hooks unregistered.");
        }
    }
}
