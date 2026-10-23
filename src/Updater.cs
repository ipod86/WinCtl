using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

namespace WinCtl
{
    /// <summary>Update über die GitHub-Releases von ipod86/WinCtl: neue exe laden, Prüfsumme prüfen, tauschen, neu starten.</summary>
    public static class Updater
    {
        const string Api = "https://api.github.com/repos/ipod86/WinCtl/releases/latest";
        public const string ReleasesPage = "https://github.com/ipod86/WinCtl/releases";

        public class Info
        {
            public string Version, Notes, Url, Sha256, Page;
            public bool Newer;
        }

        static WebClient Client()
        {
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "WinCtl/" + Program.Version;
            wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return wc;
        }

        /// <summary>"v0.2.1" / "0.2.1" -> Version (fehlende Teile = 0)</summary>
        public static Version ParseVersion(string s)
        {
            s = (s ?? "").Trim().TrimStart('v', 'V');
            int dash = s.IndexOf('-'); if (dash >= 0) s = s.Substring(0, dash);
            var parts = new List<int>();
            foreach (var p in s.Split('.')) { int n; parts.Add(int.TryParse(p, out n) ? n : 0); }
            while (parts.Count < 3) parts.Add(0);
            return new Version(parts[0], parts[1], parts[2]);
        }

        /// <summary>Fragt das neueste Release ab (wirft bei Netzwerkfehlern)</summary>
        public static Info Check()
        {
            string json;
            try
            {
                using (var wc = Client()) json = wc.DownloadString(Api);
            }
            catch (WebException ex)
            {
                var r = ex.Response as HttpWebResponse;
                if (r == null || ((int)r.StatusCode != 403 && (int)r.StatusCode != 429)) throw;
                // API rate limit (60 requests/hour per IP without login): use the redirect of the release page instead
                return CheckViaRedirect();
            }
            var d = Json.ParseObj(json);
            if (d == null) throw new CmdException("Antwort von GitHub nicht lesbar");
            var info = new Info
            {
                Version = (Str(d, "tag_name") ?? "").TrimStart('v', 'V'),
                Notes = Str(d, "body") ?? "",
                Page = Str(d, "html_url") ?? ReleasesPage
            };
            var assets = d.ContainsKey("assets") ? d["assets"] as IEnumerable : null;
            if (assets != null)
                foreach (var o in assets)
                {
                    var a = o as Dictionary<string, object>;
                    if (a == null || !string.Equals(Str(a, "name"), "WinCtl.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    info.Url = Str(a, "browser_download_url");
                    string digest = Str(a, "digest"); // "sha256:..." (GitHub)
                    if (digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) info.Sha256 = digest.Substring(7).ToLowerInvariant();
                }
            info.Newer = ParseVersion(info.Version) > ParseVersion(Program.Version);
            Last = info;
            return info;
        }

        /// <summary>Result of the last check (published as states update_available / latest_version)</summary>
        public static Info Last;
        static System.Threading.Timer periodic;
        static string notified;

        /// <summary>Checks 10 s after the start and then every 12 hours; shows a tray hint once per new version</summary>
        public static void StartPeriodic(App app)
        {
            periodic = new System.Threading.Timer(delegate
            {
                if (!app.Cfg.CheckForUpdates) return;
                try
                {
                    var info = Check(); Last = info;
                    if (info.Newer && notified != info.Version)
                    {
                        notified = info.Version;
                        Log.Info("Update verfügbar: " + info.Version);
                        Ui.Post(() => { if (Ui.Tray != null) Ui.Tray.ShowBalloonTip(8000, "WinCtl", "Version " + info.Version + " ist verfügbar - Tray-Menü \u2192 \u201eNach Update suchen\u201c", ToolTipIcon.Info); });
                    }
                }
                catch (Exception ex) { Log.Error("Update-Prüfung: " + ex.Message); }
            }, null, 10000, 12 * 3600 * 1000);
        }

        /// <summary>github.com/.../releases/latest redirects to .../tag/vX.Y.Z (no API limit, but no notes and no checksum)</summary>
        static Info CheckViaRedirect()
        {
            var req = (HttpWebRequest)WebRequest.Create(ReleasesPage + "/latest");
            req.AllowAutoRedirect = false; req.Method = "HEAD"; req.UserAgent = "WinCtl/" + Program.Version;
            string loc;
            using (var resp = (HttpWebResponse)req.GetResponse()) loc = resp.Headers["Location"] ?? "";
            int i = loc.LastIndexOf("/tag/", StringComparison.Ordinal);
            if (i < 0) throw new CmdException("Neueste Version nicht ermittelbar");
            string tag = Uri.UnescapeDataString(loc.Substring(i + 5));
            var info = new Info
            {
                Version = tag.TrimStart('v', 'V'),
                Notes = "",
                Page = loc,
                Url = ReleasesPage + "/download/" + tag + "/WinCtl.exe"
            };
            info.Newer = ParseVersion(info.Version) > ParseVersion(Program.Version);
            Last = info;
            return info;
        }

        static string Str(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v != null ? v.ToString() : null; }

        /// <summary>Lädt die neue exe, prüft sie, tauscht sie gegen die laufende und startet neu. Kehrt nur bei Fehlern zurück (Exception).</summary>
        public static void Install(App app, Info info)
        {
            if (string.IsNullOrEmpty(info.Url)) throw new CmdException("Im Release " + info.Version + " fehlt WinCtl.exe");
            string exe = Application.ExecutablePath;
            string dir = Path.GetDirectoryName(exe);
            string neu = exe + ".new", old = exe + ".old";
            try { File.Delete(neu); } catch { }
            try
            {
                using (var wc = Client()) wc.DownloadFile(info.Url, neu);
            }
            catch (UnauthorizedAccessException) { throw new CmdException("Keine Schreibrechte im Ordner " + dir + " - WinCtl in einen eigenen Ordner legen (z. B. C:\\Tools\\WinCtl)"); }

            byte[] data = File.ReadAllBytes(neu);
            if (data.Length < 10000 || data[0] != 'M' || data[1] != 'Z') { File.Delete(neu); throw new CmdException("Download ist keine gültige exe"); }
            if (info.Sha256 != null)
            {
                string got;
                using (var sha = SHA256.Create()) got = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
                if (got != info.Sha256) { File.Delete(neu); throw new CmdException("Prüfsumme stimmt nicht - Update abgebrochen"); }
            }
            // eine laufende exe darf umbenannt, aber nicht überschrieben werden
            try { File.Delete(old); } catch { }
            File.Move(exe, old);
            try { File.Move(neu, exe); }
            catch { File.Move(old, exe); throw; }

            Log.Info("Update auf " + info.Version + " installiert, starte neu");
            var args = "--wait-pid " + Process.GetCurrentProcess().Id;
            if (!string.Equals(Path.GetFullPath(app.CfgPath), Path.GetFullPath(Config.DefaultPath()), StringComparison.OrdinalIgnoreCase))
                args += " --config \"" + app.CfgPath + "\"";
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = dir });
            app.Exit();
        }

        /// <summary>Nach dem Neustart: alte exe aufräumen</summary>
        public static void Cleanup()
        {
            try { string old = Application.ExecutablePath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
            try { string neu = Application.ExecutablePath + ".new"; if (File.Exists(neu)) File.Delete(neu); } catch { }
        }

        /// <summary>Tray: prüfen und nachfragen (silent = nur melden, wenn es etwas Neues gibt)</summary>
        public static void CheckInteractive(App app, bool silent)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Info info;
                try { info = Check(); }
                catch (Exception ex)
                {
                    Log.Error("Update-Prüfung: " + ex.Message);
                    if (!silent) Ui.Post(() => MessageBox.Show("Update-Prüfung fehlgeschlagen:\n" + ex.Message, "WinCtl", MessageBoxButtons.OK, MessageBoxIcon.Warning));
                    return;
                }
                if (!info.Newer)
                {
                    if (!silent) Ui.Post(() => MessageBox.Show("WinCtl " + Program.Version + " ist aktuell.", "WinCtl", MessageBoxButtons.OK, MessageBoxIcon.Information));
                    return;
                }
                Log.Info("Update verfügbar: " + info.Version);
                if (silent)
                {
                    Ui.Post(() => { if (Ui.Tray != null) Ui.Tray.ShowBalloonTip(8000, "WinCtl", "Version " + info.Version + " ist verfügbar - Tray-Menü \u2192 \u201eNach Update suchen\u201c", ToolTipIcon.Info); });
                    return;
                }
                Ui.Post(() =>
                {
                    string notes = info.Notes.Length > 1200 ? info.Notes.Substring(0, 1200) + " ..." : info.Notes;
                    var r = MessageBox.Show("Version " + info.Version + " ist verfügbar (installiert: " + Program.Version + ").\n\n" + notes + "\n\nJetzt installieren? WinCtl startet danach neu.",
                        "WinCtl - Update", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r != DialogResult.Yes) return;
                    ThreadPool.QueueUserWorkItem(__ =>
                    {
                        try { Install(app, info); }
                        catch (Exception ex)
                        {
                            Log.Error("Update: " + ex.Message);
                            Ui.Post(() => MessageBox.Show("Update fehlgeschlagen:\n" + ex.Message, "WinCtl", MessageBoxButtons.OK, MessageBoxIcon.Error));
                        }
                    });
                });
            });
        }
    }
}
