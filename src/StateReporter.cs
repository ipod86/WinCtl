using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

namespace WinCtl
{
    /// <summary>Veröffentlicht Zustände als retained Topics {base}/{id}/state/{key} - nur bei Änderung.</summary>
    public class StateReporter
    {
        readonly App app;
        readonly Dictionary<string, string> last = new Dictionary<string, string>();
        readonly object l = new object();
        System.Threading.Timer timer; int tick; PerformanceCounter cpu; bool cpuFailed;

        public StateReporter(App a) { app = a; }

        public static object[] Describe()
        {
            Func<string, string, string, string, string, object> s = (name, type, role, unit, desc) =>
                new Dictionary<string, object> { { "name", name }, { "type", type }, { "role", role }, { "unit", unit }, { "description", desc } };
            return new[] {
                s("locked","boolean","indicator","","Bildschirm gesperrt"),
                s("user_present","boolean","indicator","","Benutzer aktiv (nicht gesperrt und Eingabe innerhalb der Idle-Schwelle)"),
                s("idle","number","value","s","Sekunden seit der letzten Maus-/Tastatureingabe"),
                s("active_window","string","text","","Titel des aktiven Fensters"),
                s("active_process","string","text","","Prozess des aktiven Fensters"),
                s("volume","number","level.volume","%","Master-Lautstärke"),
                s("muted","boolean","media.mute","","Stummgeschaltet"),
                s("cpu","number","value","%","CPU-Auslastung"),
                s("ram","number","value","%","RAM-Auslastung"),
                s("ram_free_gb","number","value","GB","Freier Arbeitsspeicher"),
                s("uptime_min","number","value","min","Laufzeit seit Windows-Start"),
                s("battery","number","value.battery","%","Akkustand (nur Geräte mit Akku)"),
                s("charging","boolean","indicator","","Netzteil angeschlossen (nur Geräte mit Akku)"),
                s("monitors","number","value","","Anzahl Monitore"),
                s("ip","string","text","","Lokale IPv4-Adresse"),
                s("user","string","text","","Angemeldeter Benutzer"),
                s("keep_awake","boolean","indicator","","keep_awake aktiv"),
                s("webcam_in_use","boolean","indicator","","Kamera wird gerade benutzt (z.B. Videokonferenz)"),
                s("webcam_app","string","text","","Programm(e), die die Kamera benutzen"),
                s("mic_in_use","boolean","indicator","","Mikrofon wird gerade benutzt"),
                s("mic_app","string","text","","Programm(e), die das Mikrofon benutzen"),
                s("update_available","boolean","indicator","","Neuere WinCtl-Version verfügbar (Prüfung beim Start und alle 12 h)"),
                s("latest_version","string","text","","Neueste WinCtl-Version auf GitHub"),
                s("disk/<laufwerk>_free_gb","number","value","GB","Freier Platz je Festplatte (dynamisch)"),
                s("disk/<laufwerk>_used_percent","number","value","%","Belegung je Festplatte (dynamisch)"),
                s("proc/<name>","boolean","indicator","","Prozess läuft (aus Config 'WatchProcesses', dynamisch)")
            };
        }

        public void Start()
        {
            // the CPU counter can take very long to initialise (perf counter cache), so never on the UI thread
            if (cpu == null && !cpuFailed)
                ThreadPool.QueueUserWorkItem(_ => { try { var c = new PerformanceCounter("Processor", "% Processor Time", "_Total"); c.NextValue(); cpu = c; } catch { cpuFailed = true; } });
            timer = new System.Threading.Timer(delegate { Tick(false); }, null, 1500, 2000);
        }

        public void Stop() { if (timer != null) timer.Dispose(); timer = null; }

        public void ForceAll()
        {
            lock (l) { last.Clear(); }
            ThreadPool.QueueUserWorkItem(_ => Tick(true));
        }

        public void SetNow(string key, object v) { Set(key, v); }

        void Set(string key, object v)
        {
            string s;
            if (v is bool) s = (bool)v ? "true" : "false";
            else if (v is double) s = ((double)v).ToString("0.#", CultureInfo.InvariantCulture);
            else s = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
            lock (l) { string o; if (last.TryGetValue(key, out o) && o == s) return; last[key] = s; }
            app.Pub("state/" + key, s, true);
        }

        static void Safe(Action a) { try { a(); } catch { } }

        void Tick(bool full)
        {
            bool slow = full || (tick++ % Math.Max(1, app.Cfg.StateIntervalSec / 2) == 0);
            var upd = Updater.Last;
            if (upd != null) { Safe(() => Set("update_available", upd.Newer)); Safe(() => Set("latest_version", upd.Version)); }
            int idle = 0;

            Safe(() => Set("locked", app.Locked));
            Safe(() => idle = Native.IdleSeconds());
            Safe(() => Set("user_present", !app.Locked && idle < app.Cfg.IdleThresholdSec));
            Safe(() =>
            {
                IntPtr h = Native.GetForegroundWindow(); uint pid; Native.GetWindowThreadProcessId(h, out pid);
                Set("active_window", app.Locked ? "" : Native.WindowTitle(h));
                string pn = ""; try { if (!app.Locked && pid != 0) pn = Process.GetProcessById((int)pid).ProcessName; } catch { }
                Set("active_process", pn);
            });
            Safe(() => { Set("volume", Audio.GetVolume()); Set("muted", Audio.GetMute()); });
            Safe(() => { var cam = Privacy.InUse("webcam"); Set("webcam_in_use", cam.Count > 0); Set("webcam_app", string.Join(", ", cam.ToArray())); });
            Safe(() => { var mic = Privacy.InUse("microphone"); Set("mic_in_use", mic.Count > 0); Set("mic_app", string.Join(", ", mic.ToArray())); });
            Safe(() => { foreach (var n in app.Cfg.WatchProcesses) { string name = n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n.Substring(0, n.Length - 4) : n; Set("proc/" + name.ToLowerInvariant(), Process.GetProcessesByName(name).Length > 0); } });

            if (!slow) return;
            Safe(() => Set("idle", idle));
            Safe(() => { var c = cpu; if (c != null) Set("cpu", Math.Round(c.NextValue())); });
            Safe(() => { double used, tot, free; Native.RamInfo(out used, out tot, out free); Set("ram", Math.Round(used)); Set("ram_free_gb", Math.Round(free, 1)); });
            Safe(() => Set("uptime_min", Native.UptimeSeconds() / 60));
            Safe(() =>
            {
                var ps = SystemInformation.PowerStatus;
                if ((ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0 && ps.BatteryLifePercent <= 1.0f)
                { Set("battery", Math.Round(ps.BatteryLifePercent * 100)); Set("charging", ps.PowerLineStatus == PowerLineStatus.Online); }
            });
            Safe(() => Set("monitors", Screen.AllScreens.Length));
            Safe(() => Set("ip", LocalIp()));
            Safe(() => Set("user", Environment.UserName));
            Safe(() =>
            {
                foreach (var d in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed && x.IsReady))
                {
                    string k = d.Name.Substring(0, 1).ToLowerInvariant();
                    Set("disk/" + k + "_free_gb", Math.Round(d.AvailableFreeSpace / 1073741824.0, 1));
                    Set("disk/" + k + "_used_percent", Math.Round(100.0 * (d.TotalSize - d.AvailableFreeSpace) / d.TotalSize));
                }
            });
        }

        /// <summary>MAC address ("AA:BB:CC:DD:EE:FF") and IPv4 broadcast address of the network card with the default gateway (for Wake-on-LAN)</summary>
        public static void LocalMac(out string mac, out string broadcast)
        {
            mac = ""; broadcast = "";
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();
                    if (props.GatewayAddresses.Count == 0) continue;
                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                        byte[] b = ni.GetPhysicalAddress().GetAddressBytes();
                        if (b.Length != 6) continue;
                        mac = BitConverter.ToString(b).Replace('-', ':');
                        byte[] ip = ua.Address.GetAddressBytes(), mask = ua.IPv4Mask.GetAddressBytes(), bc = new byte[4];
                        for (int i = 0; i < 4; i++) bc[i] = (byte)(ip[i] | ~mask[i]);
                        broadcast = new System.Net.IPAddress(bc).ToString();
                        return;
                    }
                }
            }
            catch { }
        }

        public static string LocalIp()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();
                    if (props.GatewayAddresses.Count == 0) continue;
                    foreach (var ua in props.UnicastAddresses) if (ua.Address.AddressFamily == AddressFamily.InterNetwork) return ua.Address.ToString();
                }
            }
            catch { }
            return "";
        }
    }
}
