using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WinCtl
{
    public class Config
    {
        // --- MQTT ---
        public string Host = "localhost";
        public int Port = 1883;
        public string Username = "";
        public string Password = "";
        public bool Tls = false;
        public bool TlsAllowInvalidCert = false;
        public string BaseTopic = "winctl";
        public string DeviceId = "";           // leer = Hostname

        // --- Verhalten ---
        public int StateIntervalSec = 15;
        public int IdleThresholdSec = 120;     // darunter gilt user_present = true
        public bool AllowInsecureMediaTls = false; // selbstsignierte https-Quellen (z.B. Kameras) erlauben

        // --- Sicherheit ---
        public bool AllowAnyRun = false;       // false: run/open nur für Aliase aus "Apps"
        public bool AllowShell = false;        // false: Befehl "shell" gesperrt
        public string[] DisabledCommands = new string[0];

        // --- Erweiterungen ---
        public Dictionary<string, string> Apps = new Dictionary<string, string>
        {
            { "notepad", "notepad.exe" },
            { "calc", "calc.exe" },
            { "explorer", "explorer.exe" },
            { "taskmgr", "taskmgr.exe" },
            { "cmd", "cmd.exe" },
            { "paint", "mspaint.exe" }
        };
        public Dictionary<string, string> Hotkeys = new Dictionary<string, string>();   // "ctrl+alt+1" -> "szene_kino"
        public string[] WatchProcesses = new string[0];                                  // -> state/proc/<name>
        public bool LogToFile = true;

        [System.Web.Script.Serialization.ScriptIgnore]
        public string EffectiveDeviceId
        {
            get
            {
                string id = string.IsNullOrWhiteSpace(DeviceId) ? Environment.MachineName : DeviceId;
                var sb = new StringBuilder();
                foreach (char c in id.Trim().ToLowerInvariant())
                    sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
                return sb.ToString();
            }
        }

        public static string DefaultPath()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string p1 = Path.Combine(exeDir, "winctl.config.json");
            if (File.Exists(p1)) return p1;
            try
            {
                string probe = Path.Combine(exeDir, ".w" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "x"); File.Delete(probe);
                return p1;
            }
            catch
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinCtl");
                Directory.CreateDirectory(d);
                return Path.Combine(d, "winctl.config.json");
            }
        }

        public static Config Load(string path, out bool created)
        {
            created = false;
            if (!File.Exists(path)) { var c = new Config(); c.Save(path); created = true; return c; }
            try
            {
                var c = Json.Load<Config>(File.ReadAllText(path, Encoding.UTF8));
                if (c.Apps == null) c.Apps = new Dictionary<string, string>();
                if (c.Hotkeys == null) c.Hotkeys = new Dictionary<string, string>();
                if (c.DisabledCommands == null) c.DisabledCommands = new string[0];
                if (c.WatchProcesses == null) c.WatchProcesses = new string[0];
                if (string.IsNullOrEmpty(c.BaseTopic)) c.BaseTopic = "winctl";
                return c;
            }
            catch (Exception ex)
            {
                Log.Error("Config nicht lesbar (" + ex.Message + ") - nutze Standardwerte");
                return new Config();
            }
        }

        public void Save(string path)
        {
            File.WriteAllText(path, Json.Pretty(Json.Dump(this)), new UTF8Encoding(false));
        }
    }
}
