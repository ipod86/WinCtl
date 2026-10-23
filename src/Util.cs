using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WinCtl
{
    public class CmdException : Exception
    {
        public CmdException(string msg) : base(msg) { }
    }

    public static class Json
    {
        static readonly JavaScriptSerializer S = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
        public static string Dump(object o) { return S.Serialize(o); }
        public static T Load<T>(string s) { return S.Deserialize<T>(s); }
        public static Dictionary<string, object> ParseObj(string s)
        {
            try { return S.DeserializeObject(s) as Dictionary<string, object>; }
            catch { return null; }
        }

        public static string Pretty(string json)
        {
            var sb = new StringBuilder();
            int ind = 0; bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) sb.Append(json[++i]);
                    else if (c == '"') inStr = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inStr = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(json[++i]); break; }
                        ind++; sb.Append('\n').Append(' ', ind * 2); break;
                    case '}':
                    case ']':
                        ind--; sb.Append('\n').Append(' ', Math.Max(0, ind) * 2).Append(c); break;
                    case ',': sb.Append(c).Append('\n').Append(' ', ind * 2); break;
                    case ':': sb.Append(": "); break;
                    default: if (!char.IsWhiteSpace(c)) sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>Case-insensitive, forgiving parameter bag for commands.</summary>
    public class Args
    {
        readonly Dictionary<string, object> d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public Args(Dictionary<string, object> src)
        {
            if (src != null) foreach (var kv in src) d[kv.Key] = kv.Value;
        }
        public bool Has(string k) { object v; return d.TryGetValue(k, out v) && v != null; }
        public void Set(string k, object v) { d[k] = v; }
        public object Raw(string k) { object v; d.TryGetValue(k, out v); return v; }

        public string Str(string k, string def = "")
        {
            object v = Raw(k);
            if (v == null) return def;
            if (v is bool) return ((bool)v) ? "true" : "false";
            if (v is double || v is decimal || v is float) return Convert.ToString(v, CultureInfo.InvariantCulture);
            return v.ToString();
        }
        public double Dbl(string k, double def = 0)
        {
            object v = Raw(k); if (v == null) return def;
            try
            {
                if (v is string) { double r; return double.TryParse(((string)v).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : def; }
                if (v is bool) return ((bool)v) ? 1 : 0;
                return Convert.ToDouble(v, CultureInfo.InvariantCulture);
            }
            catch { return def; }
        }
        public int Int(string k, int def = 0) { return (int)Math.Round(Dbl(k, def)); }
        public bool Bool(string k, bool def = false)
        {
            object v = Raw(k); if (v == null) return def;
            if (v is bool) return (bool)v;
            string s = v.ToString().Trim().ToLowerInvariant();
            if (s == "true" || s == "1" || s == "on" || s == "yes" || s == "an" || s == "ein" || s == "ja") return true;
            if (s == "false" || s == "0" || s == "off" || s == "no" || s == "aus" || s == "nein") return false;
            return def;
        }
        public List<string> List(string k)
        {
            var r = new List<string>(); object v = Raw(k); if (v == null) return r;
            var arr = v as System.Collections.IEnumerable;
            if (arr != null && !(v is string)) { foreach (var o in arr) r.Add(Convert.ToString(o, CultureInfo.InvariantCulture)); }
            else r.Add(v.ToString());
            return r;
        }
    }

    public static class Log
    {
        static readonly List<string> buf = new List<string>();
        static readonly object l = new object();
        static string file;
        public static event Action<string> Added;
        public static void Init(string path) { file = path; }
        public static void Info(string m) { Write("INFO ", m); }
        public static void Error(string m) { Write("ERROR", m); }
        static void Write(string lvl, string m)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + lvl + " " + m;
            lock (l)
            {
                buf.Add(line); if (buf.Count > 500) buf.RemoveRange(0, 100);
                if (file != null)
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Exists && fi.Length > 1024 * 1024) { try { File.Delete(file + ".old"); File.Move(file, file + ".old"); } catch { } }
                        File.AppendAllText(file, line + Environment.NewLine);
                    }
                    catch { }
                }
            }
            if (Console.Out != null) { try { Console.WriteLine(line); } catch { } }
            var h = Added; if (h != null) { try { h(line); } catch { } }
        }
        public static string All() { lock (l) { return string.Join(Environment.NewLine, buf.ToArray()); } }
    }

    /// <summary>Marshals work onto the WinForms UI thread.</summary>
    public static class Ui
    {
        static Control host;
        public static bool Available;
        public static NotifyIcon Tray;

        public static void Init() { host = new Control(); var h = host.Handle; Available = true; }

        /// <summary>MessageBox in front of all windows (a tray app has no window of its own, so Windows would show it in the background)</summary>
        public static DialogResult Msg(string text, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.Information)
        {
            using (var owner = new Form { TopMost = true, ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.CenterScreen, Size = new System.Drawing.Size(1, 1), Opacity = 0 })
            {
                owner.Show();
                owner.Activate();
                return MessageBox.Show(owner, text, "WinCtl", buttons, icon);
            }
        }

        public static T Send<T>(Func<T> f)
        {
            if (!Available) throw new CmdException("Diese Funktion braucht die GUI (Agent läuft im Headless-Modus)");
            if (!host.InvokeRequired) return f();
            return (T)host.Invoke(f);
        }
        public static void Send(Action a) { Send<object>(() => { a(); return null; }); }
        public static void Post(Action a)
        {
            if (!Available) { a(); return; }
            try { host.BeginInvoke(a); } catch { }
        }
        public static int S(int px)
        {
            try { using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) return (int)Math.Round(px * g.DpiX / 96.0); }
            catch { return px; }
        }
    }

    public static class Media
    {
        public static byte[] Load(string source, string base64, int maxMb = 100)
        {
            if (!string.IsNullOrEmpty(base64))
            {
                int i = base64.IndexOf("base64,", StringComparison.Ordinal);
                if (i >= 0) base64 = base64.Substring(i + 7);
                return Convert.FromBase64String(base64);
            }
            if (string.IsNullOrEmpty(source)) throw new CmdException("Parameter 'source' (Pfad, URL oder data:-URI) fehlt");
            if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return Load(null, source, maxMb);
            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var req = (HttpWebRequest)WebRequest.Create(source);
                req.Timeout = 15000; req.ReadWriteTimeout = 30000; req.UserAgent = "WinCtl";
                try
                {
                    Uri u = new Uri(source);
                    if (!string.IsNullOrEmpty(u.UserInfo))
                    {
                        var p = u.UserInfo.Split(new[] { ':' }, 2);
                        req.Credentials = new NetworkCredential(Uri.UnescapeDataString(p[0]), p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");
                        req.PreAuthenticate = true;
                    }
                }
                catch { }
                using (var resp = req.GetResponse())
                using (var s = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    var b = new byte[65536]; int n; long max = maxMb * 1024L * 1024L;
                    while ((n = s.Read(b, 0, b.Length)) > 0) { ms.Write(b, 0, n); if (ms.Length > max) throw new CmdException("Datei zu groß"); }
                    return ms.ToArray();
                }
            }
            if (!File.Exists(source)) throw new CmdException("Datei nicht gefunden: " + source);
            return File.ReadAllBytes(source);
        }

        public static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "WinCtl");
            Directory.CreateDirectory(d); return d;
        }

        /// <summary>Returns a local file path for a path/URL/base64 source (downloads if needed).</summary>
        public static string ToLocalFile(string source, string base64, string defaultExt)
        {
            if (string.IsNullOrEmpty(base64) && !string.IsNullOrEmpty(source) && File.Exists(source)) return source;
            string ext = defaultExt;
            try { if (!string.IsNullOrEmpty(source) && source.StartsWith("http", StringComparison.OrdinalIgnoreCase)) { string e = Path.GetExtension(new Uri(source).AbsolutePath); if (!string.IsNullOrEmpty(e) && e.Length <= 5) ext = e; } } catch { }
            string f = Path.Combine(TempDir(), "m_" + Guid.NewGuid().ToString("N") + ext);
            File.WriteAllBytes(f, Load(source, base64));
            return f;
        }

        public static void CleanupTemp()
        {
            try { foreach (var f in Directory.GetFiles(TempDir())) { try { if (File.GetCreationTime(f) < DateTime.Now.AddDays(-1)) File.Delete(f); } catch { } } } catch { }
        }
    }
}
