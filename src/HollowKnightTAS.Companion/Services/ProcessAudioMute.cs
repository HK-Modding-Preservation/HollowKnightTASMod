using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HollowKnightTAS.Companion.Services
{
    // Core Audio session volume only: never change game settings or the endpoint volume.
    internal sealed class ProcessAudioMute : IDisposable
    {
        private readonly int pid;
        private readonly Dictionary<string, (ISimpleAudioVolume Volume, bool Muted)> sessions = new();
        public ProcessAudioMute(int pid) => this.pid = pid;
        public void MuteNewSessions()
        {
            IMMDeviceEnumerator? devices = null;
            IMMDeviceCollection? outputs = null;
            try
            {
                devices = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                devices.EnumAudioEndpoints(0, 1, out outputs);
                outputs.GetCount(out var count);
                for (uint i = 0; i < count; i++)
                {
                    outputs.Item(i, out var device);
                    object? activated = null;
                    IAudioSessionEnumerator? enumerator = null;
                    try
                    {
                        var iid = typeof(IAudioSessionManager2).GUID;
                        device.Activate(ref iid, 23, IntPtr.Zero, out activated);
                        ((IAudioSessionManager2)activated).GetSessionEnumerator(out enumerator);
                        enumerator.GetCount(out var length);
                        for (var j = 0; j < length; j++)
                        {
                            enumerator.GetSession(j, out var control);
                            var retained = false;
                            try
                            {
                                control.GetProcessId(out var owner);
                                if (owner != pid) continue;
                                control.GetSessionInstanceIdentifier(out var id);
                                if (sessions.ContainsKey(id)) continue;
                                var volume = (ISimpleAudioVolume)control;
                                volume.GetMute(out var muted);
                                volume.SetMute(true, Guid.Empty);
                                sessions.Add(id, (volume, muted));
                                retained = true;
                            }
                            finally { if (!retained) Marshal.ReleaseComObject(control); }
                        }
                    }
                    finally
                    {
                        if (enumerator != null) Marshal.ReleaseComObject(enumerator);
                        if (activated != null) Marshal.ReleaseComObject(activated);
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
            catch (COMException exception) { System.Diagnostics.Trace.WriteLine("Restore audio: " + exception.Message); }
            finally
            {
                if (outputs != null) Marshal.ReleaseComObject(outputs);
                if (devices != null) Marshal.ReleaseComObject(devices);
            }
        }
        public void Dispose()
        {
            foreach (var session in sessions.Values)
            {
                try { session.Volume.SetMute(session.Muted, Guid.Empty); }
                catch (COMException) { /* The target may have exited on failure. */ }
                finally { Marshal.ReleaseComObject(session.Volume); }
            }
            sessions.Clear();
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator { void EnumAudioEndpoints(int flow, uint state, out IMMDeviceCollection devices); }
        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection { void GetCount(out uint count); void Item(uint index, out IMMDevice device); }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice { void Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object value); }
        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            void GetAudioSessionControl(); void GetSimpleAudioVolume();
            void GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        }
        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator { void GetCount(out int count); void GetSession(int index, out IAudioSessionControl2 session); }
        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            void GetState(); void GetDisplayName(); void SetDisplayName(); void GetIconPath(); void SetIconPath();
            void GetGroupingParam(); void SetGroupingParam(); void RegisterAudioSessionNotification(); void UnregisterAudioSessionNotification();
            void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            void GetProcessId(out int pid);
        }
        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleAudioVolume
        {
            void SetMasterVolume(float volume, [MarshalAs(UnmanagedType.LPStruct)] Guid context);
            void GetMasterVolume(out float volume);
            void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, [MarshalAs(UnmanagedType.LPStruct)] Guid context);
            void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }
    }
}
