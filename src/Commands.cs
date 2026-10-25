using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WinCtl
{
    public class ParamDef
    {
        public string Name, Type, Desc, DefaultText; public bool IsRequired; public object DefaultValue; public string[] Choices;
        public ParamDef Req() { IsRequired = true; return this; }
        public ParamDef Def(object v) { DefaultValue = v; return this; }
        public ParamDef Of(params string[] c) { Choices = c; return this; }
        public Dictionary<string, object> ToDict()
        {
            var d = new Dictionary<string, object> { { "name", Name }, { "type", Type }, { "description", Desc }, { "required", IsRequired } };
            if (DefaultValue != null) d["default"] = DefaultValue;
            if (Choices != null) d["enum"] = Choices;
            return d;
        }
    }

    public class CmdDef
    {
        public string Name, Category, Desc, Primary; public bool Dangerous, Gui; public List<ParamDef> Params = new List<ParamDef>(); public Func<Ctx, object> Run;
        /// <summary>Example payloads (JSON object or short form) for docs and the ioBroker data point description</summary>
        public string[] Examples = new string[0];
        public CmdDef Prim(string p) { Primary = p; return this; }
        public CmdDef Danger() { Dangerous = true; return this; }
        public CmdDef NeedsGui() { Gui = true; return this; }
        public Dictionary<string, object> ToDict()
        {
            var d = new Dictionary<string, object> { { "name", Name }, { "category", Category }, { "description", Desc }, { "params", Params.Select(p => p.ToDict()).ToArray() } };
            if (Primary != null) d["primary"] = Primary;
            if (Dangerous) d["dangerous"] = true;
            if (Gui) d["needs_gui"] = true;
            if (Examples.Length > 0) d["examples"] = Examples;
            return d;
        }
    }

    public class Ctx { public App App; public Args A; public Config Cfg { get { return App.Cfg; } } }

    public static class CmdRegistry
    {
        static readonly Dictionary<string, CmdDef> map = new Dictionary<string, CmdDef>(StringComparer.OrdinalIgnoreCase);
        static readonly List<CmdDef> list = new List<CmdDef>();
        static bool inited;

        public static void Init() { if (inited) return; inited = true; Commands.RegisterAll(); Commands.RegisterExamples(); }
        public static void Add(CmdDef d) { map[d.Name] = d; list.Add(d); }
        public static CmdDef Find(string name) { Init(); CmdDef d; map.TryGetValue(name, out d); return d; }
        public static object[] Describe() { Init(); return list.Select(d => (object)d.ToDict()).ToArray(); }
        public static IEnumerable<CmdDef> All() { Init(); return list; }
    }

    public static partial class Commands
    {
        static CmdDef D(string name, string cat, string desc, Func<Ctx, object> run, params ParamDef[] ps)
        {
            var d = new CmdDef { Name = name, Category = cat, Desc = desc, Run = run };
            d.Params.AddRange(ps); CmdRegistry.Add(d); return d;
        }
        static ParamDef P(string name, string type, string desc) { return new ParamDef { Name = name, Type = type, Desc = desc }; }
        static Dictionary<string, object> R(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }

        // gemeinsame Parameter für Popups
        static ParamDef[] PopupParams(bool withMode = true)
        {
            return new[] {
                P("mode","string","popup = Fenster in der Mitte, fullscreen = Vollbild, corner = Ecke (z.B. Türklingel-Kamera)").Of("popup","fullscreen","corner").Def("popup"),
                P("seconds","number","Automatisch schließen nach X Sekunden (0 = bleibt bis Klick/close_popups)"),
                P("monitor","integer","Monitor-Index (0 = erster, -1 = Hauptmonitor)").Def(-1),
                P("position","string","Lage bei mode=corner").Of("top-left","top","top-right","left","center","right","bottom-left","bottom","bottom-right").Def("bottom-right"),
                P("size","number","Breite in % des Bildschirms (5 - 100, Seitenverhältnis bleibt; leer = automatisch: Ecke 28 %, Popup max. 70 %)"),
                P("margin","integer","Abstand zum Bildschirmrand in Pixel (mode=corner)").Def(16),
                P("border","integer","Rahmenbreite in Pixel (0 = kein Rahmen)").Def(2),
                P("border_color","string","Rahmenfarbe, z.B. #ff0000").Def("#282828"),
                P("caption_size","number","Schriftgröße der Beschriftung").Def(11),
                P("caption_color","string","Farbe der Beschriftung").Def("#ffffff"),
                P("caption","string","Beschriftung unter dem Inhalt"),
                P("slot","string","Gleicher Slot ersetzt das vorherige Popup (z.B. Kamerabild aktualisieren)"),
                P("click_close","boolean","Klick schließt das Popup"),
                P("opacity","number","Deckkraft 0.1 - 1"),
                P("activate","boolean","Fokus übernehmen (Standard: nein, stört nicht beim Arbeiten)").Def(false),
                P("bg","string","Hintergrundfarbe, z.B. #000000")
            };
        }

        static string ResolveRun(Ctx c, string target)
        {
            if (string.IsNullOrWhiteSpace(target)) throw new CmdException("Parameter 'path' fehlt");
            string v;
            foreach (var kv in c.Cfg.Apps) if (string.Equals(kv.Key, target, StringComparison.OrdinalIgnoreCase)) { v = kv.Value; return v; }
            if (c.Cfg.AllowAnyRun) return target;
            throw new CmdException("'" + target + "' ist kein Alias aus 'Apps' und AllowAnyRun ist in der Config aus");
        }

        static string JoinArgs(Args a)
        {
            var raw = a.Raw("args");
            if (raw == null) return "";
            if (raw is string) return (string)raw;
            var parts = a.List("args").Select(s => s.IndexOfAny(new[] { ' ', '\t' }) >= 0 && !s.StartsWith("\"") ? "\"" + s + "\"" : s);
            return string.Join(" ", parts.ToArray());
        }

        static void Exec(string file, string args, bool hide = true)
        {
            var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = hide };
            Process.Start(psi);
        }

        public static void RegisterAll()
        {
            // ======================================================== SYSTEM
            D("ping", "system", "Lebenszeichen - antwortet mit Zeitstempel und Version", c => R("pong", true, "time", DateTime.UtcNow.ToString("o"), "version", Program.Version));
            D("get_state", "system", "Veröffentlicht alle State-Werte sofort neu", c => { c.App.Reporter.ForceAll(); return R("published", true); });
            D("reload_config", "system", "Config-Datei neu einlesen und neu verbinden", c => { ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(300); c.App.Reload(); }); return R("reloading", true); });
            D("exit", "system", "Beendet den Agent", c => { ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(300); c.App.Exit(); }); return R("exiting", true); }).Danger();
            D("list_commands", "system", "Gibt die Liste aller Befehle zurück", c => CmdRegistry.Describe());
            D("check_update", "system", "Prüft, ob es eine neuere WinCtl-Version gibt (GitHub-Releases)", c =>
            {
                var u = Updater.Check();
                return R("installed", Program.Version, "latest", u.Version, "update_available", u.Newer, "url", u.Page);
            });
            D("update", "system", "Installiert die neueste WinCtl-Version und startet den Agent neu", c =>
            {
                var u = Updater.Check();
                if (!u.Newer) return R("updated", false, "installed", Program.Version, "latest", u.Version);
                // answer first, then install (the agent restarts)
                ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(500); try { Updater.Install(c.App, u); } catch (Exception ex) { Log.Error("Update: " + ex.Message); } });
                return R("updated", true, "from", Program.Version, "to", u.Version);
            }); // only installs the official release of ipod86/WinCtl, so it gets a button in ioBroker

            // ======================================================== ENERGIE
            // shutdown/restart/logoff are normal smart home actions (can be switched off via DisabledCommands), so not "dangerous"
            D("lock", "power", "Sperrt den Bildschirm (Win+L)", c => { Native.LockWorkStation(); return null; });
            D("sleep", "power", "Standby", c => { ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(500); Native.SetSuspendState(false, true, false); }); return null; });
            D("hibernate", "power", "Ruhezustand", c => { ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(500); Native.SetSuspendState(true, true, false); }); return null; });
            D("shutdown", "power", "Fährt den PC herunter", c => { Exec("shutdown.exe", "/s /t " + Math.Max(0, c.A.Int("delay", 10)) + (c.A.Bool("force", false) ? " /f" : "")); return R("delay", c.A.Int("delay", 10)); },
                P("delay", "integer", "Verzögerung in Sekunden").Def(10), P("force", "boolean", "Programme zwangsweise schließen").Def(false)).Prim("delay");
            D("restart", "power", "Startet den PC neu", c => { Exec("shutdown.exe", "/r /t " + Math.Max(0, c.A.Int("delay", 10)) + (c.A.Bool("force", false) ? " /f" : "")); return R("delay", c.A.Int("delay", 10)); },
                P("delay", "integer", "Verzögerung in Sekunden").Def(10), P("force", "boolean", "Programme zwangsweise schließen").Def(false)).Prim("delay");
            D("logoff", "power", "Meldet den Benutzer ab", c => { Exec("shutdown.exe", "/l"); return null; });
            D("cancel_shutdown", "power", "Bricht einen geplanten Shutdown/Neustart ab", c => { Exec("shutdown.exe", "/a"); return null; });
            D("monitor_off", "power", "Schaltet die Monitore aus", c => { Native.MonitorPower(false); return null; });
            D("monitor_on", "power", "Weckt die Monitore auf", c => { Native.MonitorPower(true); return null; });
            D("screensaver", "power", "Startet den Bildschirmschoner (der in Windows eingestellte oder eine .scr-Datei)", c =>
            {
                string scr = c.A.Str("file", "");
                if (scr.Length == 0) using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop")) scr = k == null ? "" : Convert.ToString(k.GetValue("SCRNSAVE.EXE", ""));
                if (scr.Length == 0) throw new CmdException("Kein Bildschirmschoner eingestellt (Windows-Einstellungen → Sperrbildschirm → Bildschirmschoner) und kein 'file' angegeben");
                scr = Environment.ExpandEnvironmentVariables(scr);
                if (!File.Exists(scr)) throw new CmdException("Bildschirmschoner nicht gefunden: " + scr);
                Process.Start(new ProcessStartInfo(scr, "/s") { UseShellExecute = false });
                return R("file", scr);
            }, P("file", "string", "Pfad zu einer .scr-Datei (leer = der in Windows eingestellte)").Def("")).Prim("file");
            D("keep_awake", "power", "Verhindert Standby/Bildschirmschoner (bis auf Widerruf)", c =>
            {
                bool on = c.A.Bool("state", true), disp = c.A.Bool("display", true);
                Ui.Send(() => Native.SetThreadExecutionState(on ? (0x80000000u | 1u | (disp ? 2u : 0u)) : 0x80000000u));
                c.App.Reporter.SetNow("keep_awake", on);
                return R("keep_awake", on);
            }, P("state", "boolean", "an/aus").Def(true), P("display", "boolean", "auch Bildschirm anlassen").Def(true)).Prim("state").NeedsGui();

            // ======================================================== PROGRAMME
            D("run", "programs", "Startet ein Programm (Alias aus der Config oder - wenn erlaubt - beliebiger Pfad) mit Parametern", c =>
            {
                string file = ResolveRun(c, c.A.Str("path"));
                string args = JoinArgs(c.A);
                var psi = new ProcessStartInfo(file, args) { UseShellExecute = true };
                string wd = c.A.Str("workdir", ""); if (wd.Length > 0) psi.WorkingDirectory = wd;
                if (c.A.Bool("hidden", false)) psi.WindowStyle = ProcessWindowStyle.Hidden;
                else { string ws = c.A.Str("window", "normal").ToLowerInvariant(); if (ws == "minimized") psi.WindowStyle = ProcessWindowStyle.Minimized; else if (ws == "maximized") psi.WindowStyle = ProcessWindowStyle.Maximized; }
                if (c.A.Bool("admin", false)) psi.Verb = "runas";
                var p = Process.Start(psi);
                if (p == null) return R("started", true);
                if (c.A.Bool("wait", false))
                {
                    int to = c.A.Int("timeout", 60);
                    bool exited = p.WaitForExit(to * 1000);
                    return R("pid", p.Id, "exited", exited, "exit_code", exited ? (object)p.ExitCode : null);
                }
                return R("pid", p.Id);
            }, P("path", "string", "Alias (z.B. 'notepad') oder Pfad zur .exe").Req(), P("args", "string", "Parameter als String oder Liste"), P("workdir", "string", "Arbeitsverzeichnis"),
               P("hidden", "boolean", "Ohne Fenster starten"), P("window", "string", "Fensterzustand").Of("normal", "minimized", "maximized"),
               P("admin", "boolean", "Als Administrator (UAC-Abfrage am PC!)"), P("wait", "boolean", "Auf Ende warten und Exit-Code zurückgeben"), P("timeout", "integer", "Wartezeit in Sekunden bei wait").Def(60)).Prim("path").Danger();

            D("open", "programs", "Öffnet URL, Datei oder Ordner mit der Standard-App (Dateien/Ordner nur mit AllowAnyRun)", c =>
            {
                string t = c.A.Str("target");
                if (t.Length == 0) throw new CmdException("Parameter 'target' fehlt");
                bool safeUri = t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase);
                if (!safeUri) t = ResolveRun(c, t);
                Process.Start(new ProcessStartInfo(t) { UseShellExecute = true });
                return null;
            }, P("target", "string", "URL (immer erlaubt), sonst Alias/Pfad").Req()).Prim("target");

            D("kill", "programs", "Beendet Prozesse nach Name oder PID", c =>
            {
                int pid = c.A.Int("pid", 0), n = 0; string name = c.A.Str("name", "");
                if (pid > 0) { Process.GetProcessById(pid).Kill(); n = 1; }
                else
                {
                    if (name.Length == 0) throw new CmdException("name oder pid angeben");
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                    foreach (var p in Process.GetProcessesByName(name)) { try { p.Kill(); n++; } catch { } }
                }
                return R("killed", n);
            }, P("name", "string", "Prozessname, z.B. 'chrome'"), P("pid", "integer", "Prozess-ID")).Prim("name").Danger();

            D("list_processes", "programs", "Listet laufende Prozesse (max. 300)", c =>
            {
                string f = c.A.Str("filter", "").ToLowerInvariant();
                return Process.GetProcesses().Where(p => f.Length == 0 || p.ProcessName.ToLowerInvariant().Contains(f)).OrderBy(p => p.ProcessName).Take(300)
                    .Select(p => (object)R("name", p.ProcessName, "pid", p.Id, "memory_mb", (int)(SafeWs(p) / 1048576), "title", SafeTitle(p))).ToArray();
            }, P("filter", "string", "Namensfilter")).Prim("filter");

            D("list_windows", "programs", "Listet sichtbare Fenster mit Titel", c =>
                Native.Windows().Select(w => (object)R("title", w.Title, "pid", (int)w.Pid)).ToArray());
            D("focus_window", "programs", "Bringt ein Fenster (Titel enthält ...) in den Vordergrund", c => WinAct(c, 1), P("title", "string", "Teil des Fenstertitels").Req()).Prim("title");
            D("close_window", "programs", "Schließt ein Fenster sauber (Titel enthält ...)", c => WinAct(c, 2), P("title", "string", "Teil des Fenstertitels").Req()).Prim("title");
            D("minimize_all", "programs", "Alle Fenster minimieren (Desktop anzeigen) bzw. wiederherstellen", c =>
            {
                var t = Type.GetTypeFromProgID("Shell.Application"); object sh = Activator.CreateInstance(t);
                t.InvokeMember(c.A.Bool("undo", false) ? "UndoMinimizeALL" : "MinimizeAll", BindingFlags.InvokeMethod, null, sh, null);
                return null;
            }, P("undo", "boolean", "Wiederherstellen statt minimieren").Def(false));
            D("empty_recycle_bin", "programs", "Leert den Papierkorb", c => { Native.SHEmptyRecycleBin(IntPtr.Zero, null, 7); return null; });

            D("shell", "programs", "Führt einen Shell-Befehl aus und liefert die Ausgabe (nur mit AllowShell=true)", c =>
            {
                if (!c.Cfg.AllowShell) throw new CmdException("'shell' ist gesperrt - AllowShell in der Config aktivieren");
                string cmd = c.A.Str("command"); if (cmd.Length == 0) throw new CmdException("Parameter 'command' fehlt");
                bool ps = c.A.Str("shell", "powershell").ToLowerInvariant() != "cmd";
                var psi = new ProcessStartInfo(ps ? "powershell.exe" : "cmd.exe", ps ? "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(cmd)) : "/c " + cmd)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    var so = new StringBuilder(); var se = new StringBuilder();
                    p.OutputDataReceived += (s, e) => { if (e.Data != null && so.Length < 65536) so.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null && se.Length < 65536) se.AppendLine(e.Data); };
                    p.BeginOutputReadLine(); p.BeginErrorReadLine();
                    int to = c.A.Int("timeout", 30);
                    if (!p.WaitForExit(to * 1000)) { try { p.Kill(); } catch { } throw new CmdException("Timeout nach " + to + "s"); }
                    p.WaitForExit();
                    return R("exit_code", p.ExitCode, "stdout", so.ToString().TrimEnd(), "stderr", se.ToString().TrimEnd());
                }
            }, P("command", "string", "Befehl / Skript").Req(), P("shell", "string", "Interpreter").Of("powershell", "cmd").Def("powershell"), P("timeout", "integer", "Sekunden").Def(30)).Prim("command").Danger();

            // ======================================================== AUDIO
            D("volume", "audio", "Setzt die Master-Lautstärke (0-100)", c => { Audio.SetVolume(c.A.Int("level", 0)); int v = Audio.GetVolume(); c.App.Reporter.SetNow("volume", v); return R("level", v); },
                P("level", "integer", "0 - 100").Req()).Prim("level");
            D("volume_change", "audio", "Lautstärke relativ ändern", c => { int v = Math.Max(0, Math.Min(100, Audio.GetVolume() + c.A.Int("delta", 5))); Audio.SetVolume(v); c.App.Reporter.SetNow("volume", v); return R("level", v); },
                P("delta", "integer", "z.B. 5 oder -10").Def(5)).Prim("delta");
            D("mute", "audio", "Stummschalten / aufheben / umschalten", c =>
            {
                string s = c.A.Str("state", "toggle").ToLowerInvariant();
                bool m = s == "toggle" ? !Audio.GetMute() : c.A.Bool("state", true);
                Audio.SetMute(m); c.App.Reporter.SetNow("muted", m); return R("muted", m);
            }, P("state", "string", "true / false / toggle").Def("toggle")).Prim("state");
            D("media", "audio", "Sendet eine Medientaste", c =>
            {
                var keys = new Dictionary<string, byte> { { "playpause", 0xB3 }, { "play", 0xB3 }, { "pause", 0xB3 }, { "next", 0xB0 }, { "prev", 0xB1 }, { "previous", 0xB1 }, { "stop", 0xB2 }, { "volume_up", 0xAF }, { "volume_down", 0xAE }, { "mute", 0xAD } };
                string k = c.A.Str("key").ToLowerInvariant(); byte vk;
                if (!keys.TryGetValue(k, out vk)) throw new CmdException("Unbekannte Taste '" + k + "'");
                Native.Key(vk, true); return null;
            }, P("key", "string", "Medientaste").Of("playpause", "next", "prev", "stop", "volume_up", "volume_down", "mute").Req()).Prim("key");

            D("speak", "audio", "Text-to-Speech (Windows-Stimme)", c =>
            {
                string text = c.A.Str("text"); if (text.Length == 0) throw new CmdException("Parameter 'text' fehlt");
                Speech.Speak(text, c.A.Str("voice", ""), c.A.Int("rate", 0), c.A.Int("volume", 100), c.A.Bool("wait", false), c.A.Bool("interrupt", true));
                return null;
            }, P("text", "string", "Zu sprechender Text").Req(), P("voice", "string", "Teil des Stimmennamens, z.B. 'Hedda'"), P("rate", "integer", "-10 (langsam) bis 10 (schnell)").Def(0),
               P("volume", "integer", "0-100").Def(100), P("wait", "boolean", "Warten bis fertig gesprochen"), P("interrupt", "boolean", "Laufende Ausgabe abbrechen").Def(true)).Prim("text");
            D("list_voices", "audio", "Listet installierte TTS-Stimmen", c => Speech.Voices());
            D("play_sound", "audio", "Spielt eine Audiodatei (mp3/wav/wma, Pfad oder URL)", c =>
            {
                string f = Media.ToLocalFile(c.A.Str("source", c.A.Str("path", "")), c.A.Str("base64", ""), ".mp3");
                Speech.Play(f, c.A.Bool("wait", false)); return null;
            }, P("source", "string", "Pfad oder URL").Req(), P("wait", "boolean", "Warten bis zum Ende").Def(false)).Prim("source");
            D("stop_sound", "audio", "Stoppt play_sound / speak", c => { Speech.StopAll(); return null; });
            D("beep", "audio", "Einfacher Piepton", c => { Console.Beep(Math.Max(37, Math.Min(32767, c.A.Int("freq", 800))), Math.Max(10, Math.Min(5000, c.A.Int("ms", 300)))); return null; },
                P("freq", "integer", "Hz").Def(800), P("ms", "integer", "Dauer in ms").Def(300));

            // ======================================================== ANZEIGE
            D("show_image", "display", "Zeigt ein Bild als Popup, Vollbild oder Ecken-Einblendung (Pfad, URL oder Base64)", c => Popups.ShowImage(c.A),
                new[] { P("source", "string", "Pfad, http(s)-URL oder data:-URI").Req(), P("base64", "string", "Alternativ: Base64-Daten"), P("fit", "string", "Skalierung").Of("contain", "stretch", "center").Def("contain") }.Concat(PopupParams()).ToArray()).Prim("source").NeedsGui();
            D("show_video", "display", "Spielt ein Video ab (Datei oder URL; mit Edge mp4/H.264, WebM, Ogg, je nach Windows-Codecs auch HEVC)", c => Popups.ShowVideo(c.A, c.App),
                new[] { P("source", "string", "Pfad oder URL").Req(), P("loop", "boolean", "Endlosschleife").Def(false), P("muted", "boolean", "Ohne Ton").Def(false), P("volume", "number", "0 - 1").Def(1),
                        P("controls", "boolean", "Bedienelemente anzeigen").Def(false), P("close_when_done", "boolean", "Nach Ende schließen").Def(true),
                        P("engine", "string", "Player: auto = Edge (WebView2), sonst IE11; edge = nur Edge; ie = altes IE11").Of("auto", "edge", "ie").Def("auto") }.Concat(PopupParams()).ToArray()).Prim("source").NeedsGui();
            D("show_web", "display", "Zeigt eine Webseite (z.B. vis, Lovelace, Kamera-Stream) mit Edge/Chromium in einem Fenster oder als Kiosk-Vollbild, auch mehrere im Wechsel (Diashow)", c => Popups.ShowWeb(c.A, c.App),
                new[] { P("url", "string", "Adresse (oder 'urls' für eine Diashow)"), P("urls", "array", "Diashow: mehrere Adressen, wechseln nach 'interval' Sekunden"),
                        P("interval", "number", "Diashow: Sekunden je Seite").Def(30), P("zoom", "number", "Zoom, z. B. 1.25 = 125 % (nur Edge)").Def(1),
                        P("engine", "string", "Browser: auto = Edge (WebView2), sonst IE11; edge = nur Edge; ie = altes IE11").Of("auto", "edge", "ie").Def("auto") }.Concat(PopupParams()).ToArray()).Prim("url").NeedsGui();
            D("show_text", "display", "Blendet Text als Overlay ein (klickdurchlässig)", c => Popups.ShowText(c.A),
                P("text", "string", "Text").Req(), P("seconds", "number", "Anzeigedauer").Def(5), P("size", "number", "Schriftgröße").Def(40), P("color", "string", "Textfarbe").Def("#ffffff"), P("bg", "string", "Hintergrund").Def("#202020"),
                P("opacity", "number", "Deckkraft").Def(0.9), P("position", "string", "Lage").Of("top", "center", "bottom").Def("bottom"), P("monitor", "integer", "Monitor-Index").Def(-1),
                P("bold", "boolean", "Fett").Def(true), P("font", "string", "Schriftart").Def("Segoe UI"), P("click_through", "boolean", "Mausklicks gehen durch").Def(true), P("slot", "string", "Ersetzt Overlay mit gleichem Slot"),
                P("max_width", "number", "Maximale Breite in % des Bildschirms (Text bricht um)").Def(80), P("padding", "integer", "Innenabstand in Pixel").Def(24),
                P("margin", "integer", "Abstand zum Rand in Pixel (top: 40, bottom: 60)")).Prim("text").NeedsGui();
            D("ask", "display", "Zeigt einen Dialog mit Buttons/Eingabefeld und liefert die Antwort im result zurück", c => Popups.Ask(c.A),
                P("text", "string", "Fragetext").Req(), P("title", "string", "Titel"), P("buttons", "array", "Beschriftungen, z.B. [\"Ja\",\"Nein\"]").Def(new[] { "OK" }), P("input", "boolean", "Texteingabefeld anzeigen").Def(false),
                P("default", "string", "Vorgabe im Eingabefeld"), P("password", "boolean", "Eingabe verdecken").Def(false), P("timeout", "integer", "Sekunden bis automatisch geschlossen (0 = nie)").Def(0),
                P("width", "integer", "Breite des Dialogs in Pixel").Def(460), P("font_size", "number", "Schriftgröße").Def(10), P("monitor", "integer", "Monitor-Index (-1 = Hauptmonitor)").Def(-1)).Prim("text").NeedsGui();
            D("notify", "display", "Zeigt eine Windows-Benachrichtigung", c =>
            {
                Ui.Send(() =>
                {
                    if (Ui.Tray == null) throw new CmdException("Kein Tray-Icon vorhanden");
                    var icon = c.A.Str("icon", "info").ToLowerInvariant();
                    Ui.Tray.ShowBalloonTip(Math.Max(1000, c.A.Int("seconds", 5) * 1000), c.A.Str("title", "WinCtl"), c.A.Str("text", " "), icon == "error" ? ToolTipIcon.Error : (icon == "warning" ? ToolTipIcon.Warning : (icon == "none" ? ToolTipIcon.None : ToolTipIcon.Info)));
                });
                return null;
            }, P("text", "string", "Text").Req(), P("title", "string", "Titel").Def("WinCtl"), P("icon", "string", "Symbol").Of("info", "warning", "error", "none").Def("info"), P("seconds", "integer", "Dauer").Def(5)).Prim("text").NeedsGui();
            D("close_popups", "display", "Schließt alle Popups/Overlays/Dialoge von WinCtl", c => R("closed", Popups.CloseAll())).NeedsGui();

            D("screenshot", "display", "Macht einen Screenshot und veröffentlicht das Bild (JPEG/PNG-Rohdaten) auf <base>/<id>/screenshot", c =>
            {
                int mon = c.A.Int("monitor", -1);
                double scale = Math.Max(0.05, Math.Min(1.0, c.A.Dbl("scale", 0.5)));
                int q = Math.Max(10, Math.Min(100, c.A.Int("quality", 70)));
                bool png = c.A.Str("format", "jpeg").ToLowerInvariant() == "png";
                Rectangle r = mon < 0 ? SystemInformation.VirtualScreen : Popups.PickScreen(mon).Bounds;
                byte[] bytes; int w, h;
                using (var bmp = new Bitmap(r.Width, r.Height))
                {
                    try
                    {
                        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Left, r.Top, 0, 0, r.Size);
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // no access to the desktop: locked, secure desktop (UAC), or a disconnected/minimised remote desktop session
                        throw new CmdException("Kein Zugriff auf den Bildschirm - PC gesperrt, UAC-Abfrage offen oder Remotedesktop-Sitzung getrennt/minimiert");
                    }
                    w = Math.Max(1, (int)(r.Width * scale)); h = Math.Max(1, (int)(r.Height * scale));
                    using (var small = new Bitmap(w, h))
                    {
                        using (var g = Graphics.FromImage(small)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(bmp, 0, 0, w, h); }
                        using (var ms = new MemoryStream())
                        {
                            if (png) small.Save(ms, ImageFormat.Png);
                            else
                            {
                                var enc = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
                                var ep = new EncoderParameters(1); ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)q);
                                small.Save(ms, enc, ep);
                            }
                            bytes = ms.ToArray();
                        }
                    }
                }
                c.App.PubBytes("screenshot", bytes, false);
                var res = R("topic", c.App.T("screenshot"), "bytes", bytes.Length, "width", w, "height", h, "format", png ? "png" : "jpeg");
                if (c.A.Bool("inline", false)) res["base64"] = Convert.ToBase64String(bytes);
                return res;
            }, P("monitor", "integer", "-1 = alle Monitore, 0 = erster ...").Def(-1), P("scale", "number", "Verkleinerung 0.05 - 1").Def(0.5), P("quality", "integer", "JPEG-Qualität").Def(70),
               P("format", "string", "Bildformat").Of("jpeg", "png").Def("jpeg"), P("inline", "boolean", "Base64 zusätzlich im result mitsenden").Def(false)).NeedsGui();

            D("set_wallpaper", "display", "Setzt das Desktop-Hintergrundbild (Pfad oder URL)", c =>
            {
                string f = Media.ToLocalFile(c.A.Str("source"), c.A.Str("base64", ""), ".jpg");
                string keep = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinCtl"); Directory.CreateDirectory(keep);
                string dst = Path.Combine(keep, "wallpaper" + Path.GetExtension(f)); if (!string.Equals(f, dst, StringComparison.OrdinalIgnoreCase)) File.Copy(f, dst, true);
                if (!Native.SystemParametersInfo(20, 0, dst, 3)) throw new CmdException("Windows hat das Hintergrundbild abgelehnt");
                return null;
            }, P("source", "string", "Pfad oder URL").Req()).Prim("source");

            D("brightness", "display", "Setzt die Helligkeit (nur interne Laptop-Displays, 0-100)", c =>
            {
                int level = Math.Max(0, Math.Min(100, c.A.Int("level", 50)));
                var scope = new System.Management.ManagementScope(@"\\.\root\WMI");
                var q = new System.Management.ObjectQuery("SELECT * FROM WmiMonitorBrightnessMethods");
                int n = 0;
                using (var s = new System.Management.ManagementObjectSearcher(scope, q))
                    foreach (System.Management.ManagementObject o in s.Get()) { o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)level }); n++; }
                if (n == 0) throw new CmdException("Kein steuerbares Display gefunden (externe Monitore werden nicht unterstützt)");
                return R("level", level);
            }, P("level", "integer", "0 - 100").Req()).Prim("level");

            // ======================================================== EINGABE
            D("start_menu", "input", "Öffnet bzw. schließt das Startmenü (Windows-Taste)", c => { Native.Key(0x5B); return null; });
            D("send_keys", "input", "Sendet Tastendrücke (SendKeys-Syntax: ^=Strg, %=Alt, +=Umschalt, {ENTER}, {F5} ...)", c => { string k = c.A.Str("keys"); Ui.Send(() => SendKeys.SendWait(k)); return null; },
                P("keys", "string", "z.B. ^c oder %{F4} oder {ENTER}").Req()).Prim("keys").NeedsGui();
            D("type_text", "input", "Tippt Text in das aktive Fenster", c => { string t = c.A.Str("text"); Ui.Send(() => SendKeys.SendWait(EscapeKeys(t))); return null; },
                P("text", "string", "Text").Req()).Prim("text").NeedsGui();
            D("mouse", "input", "Bewegt/klickt die Maus", c =>
            {
                bool rel = c.A.Bool("relative", false);
                if (c.A.Has("x") && c.A.Has("y"))
                {
                    int x = c.A.Int("x"), y = c.A.Int("y");
                    if (rel) { Native.POINT p; Native.GetCursorPos(out p); x += p.X; y += p.Y; }
                    Native.SetCursorPos(x, y);
                }
                string click = c.A.Str("click", "none").ToLowerInvariant(); string btn = c.A.Str("button", "left").ToLowerInvariant();
                uint down = btn == "right" ? 0x8u : (btn == "middle" ? 0x20u : 0x2u), up = down << 1;
                int times = click == "double" ? 2 : (click == "none" ? 0 : 1);
                for (int i = 0; i < times; i++) { Native.mouse_event(down, 0, 0, 0, UIntPtr.Zero); Native.mouse_event(up, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(40); }
                int wheel = c.A.Int("wheel", 0); if (wheel != 0) Native.mouse_event(0x800, 0, 0, wheel * 120, UIntPtr.Zero);
                Native.POINT pos; Native.GetCursorPos(out pos);
                return R("x", pos.X, "y", pos.Y);
            }, P("x", "integer", "X-Koordinate"), P("y", "integer", "Y-Koordinate"), P("relative", "boolean", "Relativ zur aktuellen Position").Def(false),
               P("click", "string", "Klickart").Of("none", "single", "double").Def("none"), P("button", "string", "Taste").Of("left", "right", "middle").Def("left"), P("wheel", "integer", "Scrollrad-Schritte (+ hoch / - runter)"));
            D("clipboard_set", "input", "Setzt den Text der Zwischenablage", c => { string t = c.A.Str("text"); Ui.Send(() => { if (t.Length == 0) Clipboard.Clear(); else Clipboard.SetText(t); }); return null; }, P("text", "string", "Text").Req()).Prim("text").NeedsGui();
            D("clipboard_get", "input", "Liest den Text der Zwischenablage", c => R("text", Ui.Send<string>(() => Clipboard.ContainsText() ? Clipboard.GetText() : ""))).NeedsGui();
        }

        static void Ex(string name, params string[] examples) { var d = CmdRegistry.Find(name); if (d != null) d.Examples = examples; }

        /// <summary>Beispiele je Befehl (erster Eintrag = Kurzform bzw. einfachster Aufruf)</summary>
        public static void RegisterExamples()
        {
            Ex("ping", "true");
            Ex("get_state", "true");
            Ex("reload_config", "true");
            Ex("exit", "true");
            Ex("list_commands", "true");
            Ex("check_update", "true");
            Ex("update", "true");
            Ex("lock", "true");
            Ex("sleep", "true");
            Ex("hibernate", "true");
            Ex("shutdown", "60", "{\"delay\":0,\"force\":true}");
            Ex("restart", "30", "{\"delay\":0,\"force\":true}");
            Ex("logoff", "true");
            Ex("cancel_shutdown", "true");
            Ex("monitor_off", "true");
            Ex("monitor_on", "true");
            Ex("screensaver", "true", "{\"file\":\"C:/Windows/System32/Mystify.scr\"}");
            Ex("start_menu", "true");
            Ex("keep_awake", "true", "{\"state\":true,\"display\":false}");
            Ex("run", "notepad", "{\"path\":\"kodi\",\"args\":\"--fullscreen\",\"window\":\"maximized\"}");
            Ex("open", "https://www.iobroker.net", "ms-settings:display");
            Ex("kill", "notepad", "{\"pid\":1234}");
            Ex("list_processes", "chrome");
            Ex("list_windows", "true");
            Ex("focus_window", "Kodi");
            Ex("close_window", "Editor");
            Ex("minimize_all", "true", "{\"undo\":true}");
            Ex("empty_recycle_bin", "true");
            Ex("shell", "Get-Date", "{\"command\":\"ipconfig\",\"shell\":\"cmd\"}");
            Ex("volume", "30");
            Ex("volume_change", "-10");
            Ex("mute", "toggle", "true");
            Ex("media", "playpause", "next");
            Ex("speak", "Das Essen ist fertig", "{\"text\":\"Achtung\",\"voice\":\"Hedda\",\"rate\":2,\"volume\":80}");
            Ex("list_voices", "true");
            Ex("play_sound", "http://192.168.1.5/gong.mp3", "{\"source\":\"C:\\\\Sounds\\\\klingel.wav\",\"wait\":true}");
            Ex("stop_sound", "true");
            Ex("beep", "true", "{\"freq\":1200,\"ms\":500}");
            Ex("show_image", "http://kamera/snapshot.jpg",
               "{\"source\":\"http://kamera/snapshot.jpg\",\"mode\":\"corner\",\"position\":\"top-right\",\"size\":30,\"seconds\":10,\"caption\":\"Haustür\",\"slot\":\"klingel\"}",
               "{\"source\":\"https://picsum.photos/1920/1080\",\"mode\":\"fullscreen\",\"seconds\":15}");
            Ex("show_video", "http://server/video.mp4",
               "{\"source\":\"http://server/video.mp4\",\"mode\":\"corner\",\"position\":\"bottom-left\",\"size\":25,\"muted\":true,\"loop\":true}",
               "{\"source\":\"C:\\\\Videos\\\\intro.mp4\",\"mode\":\"fullscreen\",\"close_when_done\":true}");
            Ex("show_web", "http://192.168.1.5:8082/vis/",
               "{\"url\":\"http://192.168.1.5:8082/vis/\",\"mode\":\"fullscreen\"}",
               "{\"urls\":[\"http://192.168.1.5:8082/vis/\",\"http://kamera/snapshot.jpg\"],\"interval\":20,\"mode\":\"fullscreen\"}",
               "{\"url\":\"http://kamera/stream\",\"mode\":\"corner\",\"size\":35,\"seconds\":30,\"slot\":\"kamera\"}");
            Ex("show_text", "Waschmaschine fertig",
               "{\"text\":\"Alarm!\",\"size\":60,\"color\":\"#ffffff\",\"bg\":\"#c00000\",\"position\":\"center\",\"seconds\":10}");
            Ex("ask", "Licht ausschalten?",
               "{\"text\":\"Kinomodus starten?\",\"buttons\":[\"Ja\",\"Nein\"],\"timeout\":30}",
               "{\"text\":\"Wie warm soll es werden?\",\"input\":true,\"default\":\"21\",\"buttons\":[\"OK\",\"Abbrechen\"]}");
            Ex("notify", "Paket ist angekommen", "{\"text\":\"Fenster offen\",\"title\":\"Heizung\",\"icon\":\"warning\",\"seconds\":10}");
            Ex("close_popups", "true");
            Ex("screenshot", "true", "{\"monitor\":0,\"scale\":1,\"format\":\"png\"}");
            Ex("set_wallpaper", "https://picsum.photos/1920/1080");
            Ex("brightness", "60");
            Ex("send_keys", "^c", "%{F4}", "{ENTER}");
            Ex("type_text", "Hallo Welt");
            Ex("mouse", "{\"x\":100,\"y\":200,\"click\":\"single\"}", "{\"wheel\":-3}");
            Ex("clipboard_set", "Text für die Zwischenablage");
            Ex("clipboard_get", "true");
        }

        // ---------------------------------------------------------------- Hilfsfunktionen
        static long SafeWs(Process p) { try { return p.WorkingSet64; } catch { return 0; } }
        static string SafeTitle(Process p) { try { return p.MainWindowTitle; } catch { return ""; } }

        static object WinAct(Ctx c, int what)
        {
            string t = c.A.Str("title"); if (t.Length == 0) throw new CmdException("Parameter 'title' fehlt");
            var w = Native.Windows().FirstOrDefault(x => x.Title.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
            if (w == null) throw new CmdException("Kein Fenster mit '" + t + "' gefunden");
            if (what == 1) { Native.ShowWindow(w.Handle, 9); Native.SetForegroundWindow(w.Handle); }
            else Native.PostMessage(w.Handle, 0x10, IntPtr.Zero, IntPtr.Zero);
            return R("title", w.Title);
        }

        static string EscapeKeys(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s)
            {
                if ("+^%~(){}[]".IndexOf(ch) >= 0) sb.Append('{').Append(ch).Append('}');
                else if (ch == '\n') sb.Append("{ENTER}");
                else if (ch == '\r') { }
                else sb.Append(ch);
            }
            return sb.ToString();
        }
    }

    /// <summary>Text-to-Speech (SAPI, spätgebunden) und MCI-Audiowiedergabe.</summary>
    public static class Speech
    {
        static object voice; static Type vt;
        static readonly object l = new object();

        static void Ensure()
        {
            if (voice != null) return;
            vt = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (vt == null) throw new CmdException("SAPI nicht verfügbar");
            voice = Activator.CreateInstance(vt);
        }

        public static void Speak(string text, string voiceName, int rate, int volume, bool wait, bool interrupt)
        {
            lock (l)
            {
                Ensure();
                if (voiceName.Length > 0)
                {
                    object toks = vt.InvokeMember("GetVoices", BindingFlags.InvokeMethod, null, voice, null);
                    int n = (int)toks.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, toks, null);
                    for (int i = 0; i < n; i++)
                    {
                        object tok = toks.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, toks, new object[] { i });
                        string d = (string)tok.GetType().InvokeMember("GetDescription", BindingFlags.InvokeMethod, null, tok, new object[] { 0 });
                        if (d.IndexOf(voiceName, StringComparison.OrdinalIgnoreCase) >= 0) { vt.InvokeMember("Voice", BindingFlags.SetProperty, null, voice, new object[] { tok }); break; }
                    }
                }
                vt.InvokeMember("Rate", BindingFlags.SetProperty, null, voice, new object[] { Math.Max(-10, Math.Min(10, rate)) });
                vt.InvokeMember("Volume", BindingFlags.SetProperty, null, voice, new object[] { Math.Max(0, Math.Min(100, volume)) });
                int flags = (wait ? 0 : 1) | (interrupt ? 2 : 0);
                vt.InvokeMember("Speak", BindingFlags.InvokeMethod, null, voice, new object[] { text, flags });
            }
        }

        public static object[] Voices()
        {
            lock (l)
            {
                Ensure();
                var res = new List<object>();
                object toks = vt.InvokeMember("GetVoices", BindingFlags.InvokeMethod, null, voice, null);
                int n = (int)toks.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, toks, null);
                for (int i = 0; i < n; i++)
                {
                    object tok = toks.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, toks, new object[] { i });
                    res.Add((string)tok.GetType().InvokeMember("GetDescription", BindingFlags.InvokeMethod, null, tok, new object[] { 0 }));
                }
                return res.ToArray();
            }
        }

        public static void Play(string file, bool wait)
        {
            lock (l)
            {
                Native.mciSendString("close winctl_snd", null, 0, IntPtr.Zero);
                if (file.IndexOf('"') >= 0) throw new CmdException("Ungültiger Dateiname");
                int r = Native.mciSendString("open \"" + file + "\" alias winctl_snd", null, 0, IntPtr.Zero);
                if (r != 0) throw new CmdException("Audiodatei konnte nicht geöffnet werden (MCI " + r + ")");
                r = Native.mciSendString("play winctl_snd" + (wait ? " wait" : ""), null, 0, IntPtr.Zero);
                if (r != 0) throw new CmdException("Wiedergabe fehlgeschlagen (MCI " + r + ")");
                if (wait) Native.mciSendString("close winctl_snd", null, 0, IntPtr.Zero);
            }
        }

        public static void StopAll()
        {
            Native.mciSendString("close winctl_snd", null, 0, IntPtr.Zero);
            try { if (voice != null) vt.InvokeMember("Speak", BindingFlags.InvokeMethod, null, voice, new object[] { "", 3 }); } catch { }
        }
    }
}
