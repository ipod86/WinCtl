using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinCtl
{
    /// <summary>
    /// Zentrale Klasse: MQTT-Protokoll, Befehls-Dispatch, Info-Manifest.
    /// Topic-Schema:  {base}/{id}/online | info | state/* | event/* | cmd/{name} | result | screenshot
    ///                {base}/all/cmd/{name}   (Broadcast an alle Agenten)
    /// </summary>
    public class App
    {
        public Config Cfg;
        public string CfgPath;
        public MqttClient Mqtt;
        public StateReporter Reporter;
        public bool Headless;
        public bool Locked;
        public readonly ManualResetEvent ExitEvent = new ManualResetEvent(false);
        public event Action<bool> ConnectionChanged;
        public event Action ConfigReloaded;
        HotkeyManager hot;
        readonly DateTime started = DateTime.UtcNow;
        bool eventsHooked;

        public string Id { get { return Cfg.EffectiveDeviceId; } }
        public string Base { get { return Cfg.BaseTopic.Trim('/'); } }

        public App(string cfgPath, bool headless) { CfgPath = cfgPath; Headless = headless; }

        public bool LoadConfig()
        {
            bool created; Cfg = Config.Load(CfgPath, out created);
            return created;
        }

        public string T(string suffix) { return Base + "/" + Id + "/" + suffix; }

        public void Pub(string suffix, string payload, bool retain)
        {
            var m = Mqtt; if (m != null) m.Publish(T(suffix), payload, retain);
        }
        public void PubBytes(string suffix, byte[] data, bool retain)
        {
            var m = Mqtt; if (m != null) m.Publish(T(suffix), data, retain);
        }
        public void PubEvent(string name, object data)
        {
            var d = new Dictionary<string, object> { { "ts", DateTime.UtcNow.ToString("o") } };
            var dd = data as Dictionary<string, object>;
            if (dd != null) foreach (var kv in dd) d[kv.Key] = kv.Value;
            Pub("event/" + name, Json.Dump(d), false);
        }

        // ------------------------------------------------------------------ Lifecycle
        public void Start()
        {
            CmdRegistry.Init();
            if (Cfg.AllowInsecureMediaTls)
                System.Net.ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            try { System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072; } catch { }

            Mqtt = new MqttClient
            {
                Host = Cfg.Host, Port = Cfg.Port, User = Cfg.Username, Pass = Cfg.Password, Tls = Cfg.Tls, TlsInsecure = Cfg.TlsAllowInvalidCert,
                ClientId = "winctl_" + Id, WillTopic = T("online"), WillPayload = "false", WillRetain = true, KeepAlive = 30
            };
            Mqtt.OnConnected += OnConnected;
            Mqtt.OnDisconnected += delegate { var h = ConnectionChanged; if (h != null) h(false); };
            Mqtt.OnMessage += OnMessage;
            Mqtt.Subscribe(T("cmd/#"));
            Mqtt.Subscribe(Base + "/all/cmd/#");
            Mqtt.Start();

            Reporter = new StateReporter(this);
            Reporter.Start();

            if (!Headless)
            {
                Ui.Send(() => { hot = new HotkeyManager(this); hot.Register(Cfg.Hotkeys); }); // muss im UI-Thread liegen
                if (!eventsHooked)
                {
                    eventsHooked = true;
                    SystemEvents.SessionSwitch += OnSession;
                    SystemEvents.PowerModeChanged += OnPower;
                }
            }
            Log.Info("Agent gestartet - Geräte-ID '" + Id + "', Topic-Basis '" + Base + "/" + Id + "'");
        }

        public void Stop(bool announceOffline)
        {
            try { if (Reporter != null) Reporter.Stop(); } catch { }
            try { var h0 = hot; hot = null; if (h0 != null) Ui.Send(() => h0.Dispose()); } catch { }
            var m = Mqtt;
            if (m != null)
            {
                if (announceOffline) m.Publish(T("online"), "false", true);
                Thread.Sleep(100);
                m.Stop();
            }
            Mqtt = null;
        }

        public void Reload()
        {
            Log.Info("Config wird neu geladen ...");
            lock (Lifecycle) { Stop(true); LoadConfig(); Start(); }
            var h = ConfigReloaded; if (h != null) Ui.Post(h);
        }

        /// <summary>serialises Start/Restart/Reload (the first start runs in the background, saving the settings may restart at the same time)</summary>
        public readonly object Lifecycle = new object();

        public void Restart() { lock (Lifecycle) { Stop(true); Start(); } var h = ConfigReloaded; if (h != null) Ui.Post(h); }

        public void Exit()
        {
            Log.Info("Beende Agent");
            try { if (eventsHooked) { SystemEvents.SessionSwitch -= OnSession; SystemEvents.PowerModeChanged -= OnPower; } } catch { }
            Stop(true);
            ExitEvent.Set();
            if (!Headless) Ui.Post(() => { if (Ui.Tray != null) Ui.Tray.Visible = false; Application.Exit(); });
        }

        void OnConnected()
        {
            Pub("online", "true", true);
            Pub("info", Json.Dump(Info()), true);
            Reporter.ForceAll();
            var h = ConnectionChanged; if (h != null) h(true);
        }

        void OnSession(object s, SessionSwitchEventArgs e)
        {
            string ev = null;
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock: Locked = true; ev = "lock"; break;
                case SessionSwitchReason.SessionUnlock: Locked = false; ev = "unlock"; break;
                case SessionSwitchReason.SessionLogon: ev = "logon"; break;
                case SessionSwitchReason.SessionLogoff: ev = "logoff"; break;
                case SessionSwitchReason.RemoteConnect: ev = "remote_connect"; break;
                case SessionSwitchReason.RemoteDisconnect: ev = "remote_disconnect"; break;
            }
            if (ev != null) { PubEvent(ev, null); if (Reporter != null) Reporter.SetNow("locked", Locked); }
        }

        void OnPower(object s, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend) PubEvent("suspend", null);
            else if (e.Mode == PowerModes.Resume) { PubEvent("resume", null); ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(5000); if (Reporter != null) Reporter.ForceAll(); }); }
        }

        // ------------------------------------------------------------------ Befehle
        void OnMessage(string topic, byte[] payload)
        {
            string p1 = T("cmd/"), p2 = Base + "/all/cmd/", name = null;
            if (topic.StartsWith(p1, StringComparison.Ordinal)) name = topic.Substring(p1.Length);
            else if (topic.StartsWith(p2, StringComparison.Ordinal)) name = topic.Substring(p2.Length);
            if (string.IsNullOrEmpty(name) || name.Contains("/")) return;
            string body = Encoding.UTF8.GetString(payload);
            ThreadPool.QueueUserWorkItem(_ => Execute(name, body));
        }

        public void Execute(string name, string body)
        {
            string id = null; object data = null; string err = null; bool ok = false;
            try
            {
                body = (body ?? "").Trim();
                Dictionary<string, object> raw = null;
                if (body.StartsWith("{")) raw = Json.ParseObj(body);
                var a = new Args(raw);
                var def = CmdRegistry.Find(name);
                if (def == null) throw new CmdException("Unbekannter Befehl '" + name + "'");
                if (body.Length > 0 && raw == null && !body.StartsWith("{"))
                {
                    if (def.Primary != null) a.Set(def.Primary, body.Trim('"'));
                }
                else if (raw != null && a.Has("value") && def.Primary != null && !a.Has(def.Primary)) a.Set(def.Primary, a.Raw("value"));
                id = a.Has("id") ? a.Str("id") : null;
                if (Cfg.DisabledCommands.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase))) throw new CmdException("Befehl '" + name + "' ist in der Config deaktiviert");
                if (def.Gui && !Ui.Available) throw new CmdException("Befehl braucht die GUI (Headless-Modus)");
                Log.Info("Befehl: " + name + (body.Length > 0 && body.Length < 200 && !body.Contains("base64") ? " " + body : ""));
                data = def.Run(new Ctx { App = this, A = a });
                ok = true;
            }
            catch (CmdException ex) { err = ex.Message; Log.Error(name + ": " + err); }
            catch (System.Reflection.TargetInvocationException ex) { err = (ex.InnerException ?? ex).Message; Log.Error(name + ": " + err); }
            catch (Exception ex) { err = ex.GetType().Name + ": " + ex.Message; Log.Error(name + ": " + err); }

            var res = new Dictionary<string, object> { { "id", id }, { "cmd", name }, { "ok", ok }, { "ts", DateTime.UtcNow.ToString("o") } };
            if (ok) res["data"] = data; else res["error"] = err;
            Pub("result", Json.Dump(res), false);
        }

        // ------------------------------------------------------------------ Info-Manifest
        public Dictionary<string, object> Info()
        {
            string mac, broadcast; StateReporter.LocalMac(out mac, out broadcast);
            return new Dictionary<string, object>
            {
                { "protocol", 1 },
                { "app", "WinCtl" },
                { "version", Program.Version },
                { "device_id", Id },
                { "base_topic", Base },
                { "hostname", Environment.MachineName },
                { "user", Environment.UserName },
                { "os", Environment.OSVersion.VersionString },
                { "ip", StateReporter.LocalIp() },
                { "mac", mac },
                { "broadcast", broadcast },
                { "started", started.ToString("o") },
                { "topics", new Dictionary<string, object> {
                    { "online", T("online") }, { "info", T("info") }, { "state", T("state/<key>") }, { "event", T("event/<name>") },
                    { "command", T("cmd/<command>") }, { "broadcast", Base + "/all/cmd/<command>" }, { "result", T("result") }, { "screenshot", T("screenshot") } } },
                { "security", new Dictionary<string, object> { { "allow_any_run", Cfg.AllowAnyRun }, { "allow_shell", Cfg.AllowShell }, { "apps", Cfg.Apps.Keys.ToArray() }, { "disabled_commands", Cfg.DisabledCommands } } },
                { "hotkeys", Cfg.Hotkeys },
                { "states", StateReporter.Describe() },
                { "events", new object[] { "lock", "unlock", "logon", "logoff", "remote_connect", "remote_disconnect", "suspend", "resume", "hotkey" } },
                { "commands", CmdRegistry.Describe() }
            };
        }
    }

    public class HotkeyManager : NativeWindow, IDisposable
    {
        readonly App app; readonly Dictionary<int, string> ids = new Dictionary<int, string>(); readonly Dictionary<int, string> specs = new Dictionary<int, string>();
        public HotkeyManager(App a) { app = a; CreateHandle(new CreateParams()); }

        /// <summary>"ctrl+alt+1" -> RegisterHotKey-Modifier und Taste (auch für die Prüfung im Einstellungsdialog)</summary>
        public static bool TryParse(string spec, out uint mod, out Keys key)
        {
            mod = 0x4000; key = Keys.None; bool ok = true;
            foreach (var tok in (spec ?? "").ToLowerInvariant().Split('+'))
            {
                string t = tok.Trim();
                if (t == "ctrl" || t == "strg") mod |= 2; else if (t == "alt") mod |= 1; else if (t == "shift") mod |= 4; else if (t == "win") mod |= 8;
                else
                {
                    Keys k;
                    if (t.Length == 1 && char.IsDigit(t[0])) key = Keys.D0 + (t[0] - '0');
                    else if (Enum.TryParse<Keys>(t, true, out k)) key = k; else ok = false;
                }
            }
            return ok && key != Keys.None;
        }

        public void Register(Dictionary<string, string> map)
        {
            int n = 1;
            foreach (var kv in map)
            {
                uint mod; Keys key; bool okk = TryParse(kv.Key, out mod, out key);
                if (!okk) { Log.Error("Hotkey '" + kv.Key + "' nicht erkannt"); continue; }
                if (Native.RegisterHotKey(Handle, n, mod, (uint)key)) { ids[n] = kv.Value; specs[n] = kv.Key; n++; }
                else Log.Error("Hotkey '" + kv.Key + "' konnte nicht registriert werden (bereits belegt?)");
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                int id = m.WParam.ToInt32(); string name;
                if (ids.TryGetValue(id, out name)) app.PubEvent("hotkey", new Dictionary<string, object> { { "name", name }, { "keys", specs[id] } });
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            foreach (var id in ids.Keys.ToArray()) Native.UnregisterHotKey(Handle, id);
            ids.Clear();
            try { DestroyHandle(); } catch { }
        }
    }
}
