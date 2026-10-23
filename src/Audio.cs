using System;
using System.Runtime.InteropServices;

namespace WinCtl
{
    /// <summary>Master-Lautstärke und Mute über Core Audio (IAudioEndpointVolume).</summary>
    public static class Audio
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            void EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            void GetDevice(string id, out IMMDevice device);
            void RegisterEndpointNotificationCallback(IntPtr client);
            void UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            void Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            void OpenPropertyStore(int access, out IntPtr props);
            void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            void GetState(out int state);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            void RegisterControlChangeNotify(IntPtr n);
            void UnregisterControlChangeNotify(IntPtr n);
            void GetChannelCount(out uint count);
            void SetMasterVolumeLevel(float db, ref Guid ctx);
            void SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            void GetMasterVolumeLevel(out float db);
            void GetMasterVolumeLevelScalar(out float level);
            void SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
            void SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
            void GetChannelVolumeLevel(uint ch, out float db);
            void GetChannelVolumeLevelScalar(uint ch, out float level);
            void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
            void GetVolumeStepInfo(out uint step, out uint count);
            void VolumeStepUp(ref Guid ctx);
            void VolumeStepDown(ref Guid ctx);
            void QueryHardwareSupport(out uint mask);
            void GetVolumeRange(out float min, out float max, out float inc);
        }

        static IAudioEndpointVolume Ep()
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice dev; en.GetDefaultAudioEndpoint(0, 1, out dev);
            Guid iid = typeof(IAudioEndpointVolume).GUID; object o;
            dev.Activate(ref iid, 23, IntPtr.Zero, out o);
            return (IAudioEndpointVolume)o;
        }

        public static int GetVolume() { float v; Ep().GetMasterVolumeLevelScalar(out v); return (int)Math.Round(v * 100); }
        public static void SetVolume(int percent)
        {
            if (percent < 0) percent = 0; if (percent > 100) percent = 100;
            Guid g = Guid.Empty; Ep().SetMasterVolumeLevelScalar(percent / 100f, ref g);
        }
        public static bool GetMute() { bool m; Ep().GetMute(out m); return m; }
        public static void SetMute(bool mute) { Guid g = Guid.Empty; Ep().SetMute(mute, ref g); }
    }
}
