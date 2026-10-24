using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace WinCtl
{
    /// <summary>
    /// Which program uses the camera or the microphone right now. Windows keeps this in the registry
    /// (the same data as the camera/microphone icon in the taskbar): an entry is in use while LastUsedTimeStop is 0.
    /// </summary>
    public static class Privacy
    {
        const string Store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

        /// <summary>Names of the programs that use the capability ("webcam" or "microphone") at the moment</summary>
        public static List<string> InUse(string capability)
        {
            var apps = new List<string>();
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using (var k = root.OpenSubKey(Store + capability))
                    {
                        if (k == null) continue;
                        Scan(k, apps, false);
                        using (var np = k.OpenSubKey("NonPackaged")) if (np != null) Scan(np, apps, true);
                    }
                }
                catch { }
            }
            return apps;
        }

        static void Scan(RegistryKey parent, List<string> apps, bool nonPackaged)
        {
            foreach (var name in parent.GetSubKeyNames())
            {
                if (name == "NonPackaged") continue;
                try
                {
                    using (var k = parent.OpenSubKey(name))
                    {
                        if (k == null) continue;
                        long start = ToLong(k.GetValue("LastUsedTimeStart")), stop = ToLong(k.GetValue("LastUsedTimeStop"));
                        if (start > 0 && stop == 0)
                        {
                            string app = AppName(name, nonPackaged);
                            if (!apps.Contains(app)) apps.Add(app);
                        }
                    }
                }
                catch { }
            }
        }

        static long ToLong(object v) { try { return v == null ? 0 : Convert.ToInt64(v); } catch { return 0; } }

        /// <summary>"C:#Program Files#Zoom#bin#Zoom.exe" -> "Zoom", "Microsoft.WindowsCamera_8wekyb3d8bbwe" -> "Microsoft.WindowsCamera"</summary>
        public static string AppName(string key, bool nonPackaged)
        {
            if (nonPackaged)
            {
                string file = key.Substring(key.LastIndexOf('#') + 1);
                return Path.GetFileNameWithoutExtension(file);
            }
            int us = key.IndexOf('_');
            return us > 0 ? key.Substring(0, us) : key;
        }
    }
}
