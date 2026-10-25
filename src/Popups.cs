using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WinCtl
{
    /// <summary>Rahmenloses Topmost-Fenster, das standardmäßig keinen Fokus klaut.</summary>
    public class PopupBase : Form
    {
        public string Slot = "";
        public bool NoActivate = true;
        public bool ClickThrough = false;
        public string TempFile;

        public PopupBase()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; KeyPreview = true;
            BackColor = Color.FromArgb(40, 40, 40); Padding = new Padding(2);
        }
        protected override bool ShowWithoutActivation { get { return NoActivate; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW: kein Alt-Tab-Eintrag
                if (NoActivate) cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                if (ClickThrough) cp.ExStyle |= 0x20 | 0x80000; // TRANSPARENT | LAYERED
                return cp;
            }
        }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); base.OnKeyDown(e); }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Popups.Forget(this);
            if (TempFile != null) { try { File.Delete(TempFile); } catch { } }
            base.OnFormClosed(e);
        }
    }

    public static class Popups
    {
        static readonly List<Form> all = new List<Form>(); // nur UI-Thread

        public static void Forget(Form f) { all.Remove(f); }

        public static Screen PickScreen(int idx)
        {
            var s = Screen.AllScreens;
            if (idx < 0 || idx >= s.Length) return Screen.PrimaryScreen;
            return s[idx];
        }

        static Color Col(string html, Color def)
        {
            if (string.IsNullOrEmpty(html)) return def;
            try { return ColorTranslator.FromHtml(html); } catch { return def; }
        }

        static void ReplaceSlot(string slot)
        {
            if (string.IsNullOrEmpty(slot)) return;
            foreach (var f in all.ToArray()) { var p = f as PopupBase; if (p != null && p.Slot == slot) p.Close(); }
        }

        static void HookClick(Control c, EventHandler h) { c.Click += h; foreach (Control ch in c.Controls) HookClick(ch, h); }

        /// <summary>Lage und Größe: size = Breite in % des Bildschirms (Seitenverhältnis bleibt), margin = Abstand zum Rand in px</summary>
        static Rectangle Layout(string mode, Screen sc, Size content, string position, int extraH, double sizePct, int margin)
        {
            Rectangle wa = sc.WorkingArea;
            if (mode == "fullscreen") return sc.Bounds;
            double w = Math.Max(1, content.Width), h = Math.Max(1, content.Height);
            double scale;
            if (sizePct > 0)
            {
                // fixed width in % of the screen (also enlarges small content), never higher than the screen
                double pct = Math.Max(5, Math.Min(100, sizePct)) / 100.0;
                scale = Math.Min(wa.Width * pct / w, (wa.Height - extraH - 2 * margin) / h);
            }
            else
            {
                double maxW = wa.Width * (mode == "corner" ? 0.28 : 0.7), maxH = wa.Height * (mode == "corner" ? 0.35 : 0.7);
                scale = Math.Min(maxW / w, maxH / h);
                if (mode != "corner") scale = Math.Min(scale, 1.0);
            }
            w = Math.Round(w * scale) + 4; h = Math.Round(h * scale) + 4 + extraH;
            int x, y, m = Math.Max(0, margin);
            position = (position ?? "").ToLowerInvariant();
            if (mode == "corner")
            {
                if (position.Length == 0) position = "bottom-right";
                x = position.EndsWith("left") ? wa.Left + m : (position.EndsWith("right") ? wa.Right - (int)w - m : wa.Left + (wa.Width - (int)w) / 2);
                y = position.StartsWith("top") ? wa.Top + m : (position.StartsWith("bottom") ? wa.Bottom - (int)h - m : wa.Top + (wa.Height - (int)h) / 2);
            }
            else { x = wa.Left + (wa.Width - (int)w) / 2; y = wa.Top + (wa.Height - (int)h) / 2; }
            return new Rectangle(x, y, (int)w, (int)h);
        }

        /// <summary>Gemeinsames Finish: Platzieren, Caption, Click-to-close, Auto-Close, anzeigen.</summary>
        static void Present(PopupBase f, Control content, Args a, string mode, Size contentSize, double defSeconds, bool defClickClose)
        {
            string caption = a.Str("caption", "");
            var bg = Col(a.Str("bg", ""), Color.Black);
            content.BackColor = bg;
            content.Dock = DockStyle.Fill;
            Label cap = null;
            if (caption.Length > 0)
            {
                float capSize = (float)Math.Max(6, Math.Min(72, a.Dbl("caption_size", 11)));
                cap = new Label { Text = caption, Dock = DockStyle.Bottom, Height = (int)(capSize * 3.1), ForeColor = Col(a.Str("caption_color", "#ffffff"), Color.White), BackColor = Color.FromArgb(30, 30, 30), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", capSize) };
                f.Controls.Add(cap);
            }
            f.Controls.Add(content); content.BringToFront();

            f.Slot = a.Str("slot", "");
            f.Text = a.Str("title", "WinCtl");
            f.NoActivate = !a.Bool("activate", false);
            double op = a.Dbl("opacity", 1); if (op > 0 && op < 1) f.Opacity = op;
            if (mode == "fullscreen") f.Padding = new Padding(0);
            else
            {
                // frame around the content: width and colour
                f.Padding = new Padding(Math.Max(0, Math.Min(40, a.Int("border", 2))));
                f.BackColor = Col(a.Str("border_color", "#282828"), Color.FromArgb(40, 40, 40));
            }

            Screen sc = PickScreen(a.Int("monitor", -1));
            f.Bounds = Layout(mode, sc, contentSize, a.Str("position", "bottom-right"), cap != null ? cap.Height : 0, a.Dbl("size", 0), a.Int("margin", 16));

            if (a.Bool("click_close", defClickClose)) HookClick(f, delegate { f.Close(); });
            ReplaceSlot(f.Slot);
            all.Add(f);
            f.Show();
            if (!f.NoActivate) f.Activate();

            double sec = a.Dbl("seconds", defSeconds);
            if (sec > 0)
            {
                var t = new System.Windows.Forms.Timer { Interval = (int)(sec * 1000) };
                t.Tick += delegate { t.Stop(); t.Dispose(); if (!f.IsDisposed) f.Close(); };
                t.Start();
            }
        }

        // ------------------------------------------------------------ Bild
        public static object ShowImage(Args a)
        {
            byte[] data = Media.Load(a.Str("source", a.Str("url", a.Str("path", ""))), a.Str("base64", ""));
            string mode = a.Str("mode", "popup").ToLowerInvariant();
            return Ui.Send<object>(() =>
            {
                Image img;
                try { img = Image.FromStream(new MemoryStream(data)); }
                catch { throw new CmdException("Kein gültiges Bildformat"); }
                var f = new PopupBase();
                var fit = a.Str("fit", "contain").ToLowerInvariant();
                var pb = new PictureBox { Image = img, SizeMode = fit == "stretch" ? PictureBoxSizeMode.StretchImage : (fit == "center" ? PictureBoxSizeMode.CenterImage : PictureBoxSizeMode.Zoom) };
                f.FormClosed += delegate { pb.Image = null; img.Dispose(); };
                Present(f, pb, a, mode, img.Size, 10, true);
                return new Dictionary<string, object> { { "width", img.Width }, { "height", img.Height }, { "mode", mode } };
            });
        }

        // ------------------------------------------------------------ Video
        public static object ShowVideo(Args a, App app)
        {
            string src = a.Str("source", a.Str("url", a.Str("path", "")));
            if (src.Length == 0) throw new CmdException("Parameter 'source' (Pfad oder URL) fehlt");
            string mode = a.Str("mode", "popup").ToLowerInvariant();
            string uri;
            if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) uri = src;
            else { if (!File.Exists(src)) throw new CmdException("Datei nicht gefunden: " + src); uri = new Uri(Path.GetFullPath(src)).AbsoluteUri; }

            var html = new StringBuilder();
            html.Append("<!DOCTYPE html>\r\n<!-- saved from url=(0014)about:internet -->\r\n<html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">");
            html.Append("<style>html,body{margin:0;padding:0;height:100%;background:#000;overflow:hidden}video{width:100%;height:100%;background:#000}</style></head><body>");
            html.Append("<video id=\"v\" src=\"" + System.Web.HttpUtility.HtmlAttributeEncode(uri) + "\" autoplay");
            if (a.Bool("loop", false)) html.Append(" loop");
            if (a.Bool("muted", false)) html.Append(" muted");
            if (a.Bool("controls", false)) html.Append(" controls");
            html.Append("></video><script>var v=document.getElementById('v');v.volume=" + Math.Max(0, Math.Min(1, a.Dbl("volume", 1))).ToString(System.Globalization.CultureInfo.InvariantCulture) + ";");
            html.Append("v.onended=function(){document.title='WINCTL_ENDED';};v.onerror=function(){document.title='WINCTL_ERROR';};</script></body></html>");
            string tmp = Path.Combine(Media.TempDir(), "v_" + Guid.NewGuid().ToString("N") + ".html");
            File.WriteAllText(tmp, html.ToString(), new UTF8Encoding(true));
            bool autoClose = a.Bool("close_when_done", true) && !a.Bool("loop", false);
            string engine = a.Str("engine", "auto").ToLowerInvariant();
            string edgeVersion = engine == "ie" ? null : WebEngine.Version();
            if (engine == "edge" && edgeVersion == null) throw new CmdException("WebView2-Runtime ist nicht installiert - ohne 'engine' wird IE11 genutzt");
            bool edge = edgeVersion != null;
            bool insecure = app.Cfg.AllowInsecureMediaTls;

            return Ui.Send<object>(() =>
            {
                var f = new PopupBase { TempFile = tmp };
                Action<string> onTitle = title =>
                {
                    if (title == "WINCTL_ENDED" && autoClose) f.Close();
                    else if (title == "WINCTL_ERROR") { Log.Error("Video konnte nicht abgespielt werden: " + src); f.Close(); }
                };
                Control view;
                if (edge) view = Edge.Create(new Uri(tmp).AbsoluteUri, 1, insecure, null, onTitle);
                else
                {
                    var wb = NewBrowser(); view = wb;
                    wb.DocumentTitleChanged += delegate { onTitle(wb.DocumentTitle); };
                }
                Present(f, view, a, mode, new Size(1280, 720), 0, a.Bool("click_close", true));
                if (!edge) ((WebBrowser)view).Navigate(new Uri(tmp));
                var r = new Dictionary<string, object> { { "mode", mode }, { "engine", edge ? "edge" : "ie" } };
                if (edge) r["engine_version"] = edgeVersion;
                return r;
            });
        }

        // ------------------------------------------------------------ Webseite
        public static object ShowWeb(Args a, App app)
        {
            var urls = new List<string>();
            if (a.Raw("urls") != null) urls.AddRange(a.List("urls"));
            string one = a.Str("url", a.Str("source", ""));
            if (one.Length > 0) urls.Insert(0, one);
            urls = urls.Select(u => u.Trim()).Where(u => u.Length > 0).Select(u => u.Contains("://") ? u : "http://" + u).ToList();
            if (urls.Count == 0) throw new CmdException("Parameter 'url' (oder 'urls' für eine Diashow) fehlt");
            int interval = (int)Math.Max(3, a.Dbl("interval", 30));
            double zoom = Math.Max(0.25, Math.Min(5, a.Dbl("zoom", 1)));
            string mode = a.Str("mode", "popup").ToLowerInvariant();
            string engine = a.Str("engine", "auto").ToLowerInvariant();
            string edgeVersion = engine == "ie" ? null : WebEngine.Version();
            if (engine == "edge" && edgeVersion == null) throw new CmdException("WebView2-Runtime ist nicht installiert (https://go.microsoft.com/fwlink/p/?LinkId=2124703) - ohne 'engine' wird IE11 genutzt");
            bool edge = edgeVersion != null;
            bool insecure = app.Cfg.AllowInsecureMediaTls;
            Action<string> onUrl = u => app.Reporter.SetNow("web_url", u);
            return Ui.Send<object>(() =>
            {
                var f = new PopupBase();
                Control view; Action<string> go;
                if (edge) { view = Edge.Create(urls[0], zoom, insecure, onUrl); go = u => Edge.Navigate(view, u); }
                else
                {
                    var wb = NewBrowser(); view = wb; go = u => wb.Navigate(u);
                    wb.DocumentCompleted += (s, e) => { if (e.Url == wb.Url) onUrl(wb.Url.AbsoluteUri); };
                }
                f.FormClosed += delegate { onUrl(""); };
                Present(f, view, a, mode, new Size(1280, 720), 0, a.Bool("click_close", false));
                if (!edge) go(urls[0]);
                if (urls.Count > 1)
                {
                    // slideshow: next page every 'interval' seconds, endlessly until the popup is closed
                    int i = 0;
                    var t = new System.Windows.Forms.Timer { Interval = interval * 1000 };
                    t.Tick += delegate { i = (i + 1) % urls.Count; go(urls[i]); };
                    f.FormClosed += delegate { t.Dispose(); };
                    t.Start();
                }
                var r = new Dictionary<string, object> { { "mode", mode }, { "pages", urls.Count }, { "engine", edge ? "edge" : "ie" } };
                if (edge) r["engine_version"] = edgeVersion;
                return r;
            });
        }

        static WebBrowser NewBrowser()
        {
            return new WebBrowser { ScriptErrorsSuppressed = true, AllowWebBrowserDrop = false, IsWebBrowserContextMenuEnabled = false, WebBrowserShortcutsEnabled = false };
        }

        // ------------------------------------------------------------ Text-Overlay
        public static object ShowText(Args a)
        {
            string text = a.Str("text", "");
            if (text.Length == 0) throw new CmdException("Parameter 'text' fehlt");
            return Ui.Send<object>(() =>
            {
                var f = new PopupBase { ClickThrough = a.Bool("click_through", true) };
                f.NoActivate = true;
                var font = new Font(a.Str("font", "Segoe UI"), (float)a.Dbl("size", 40), a.Bool("bold", true) ? FontStyle.Bold : FontStyle.Regular);
                Screen sc = PickScreen(a.Int("monitor", -1));
                Rectangle wa = sc.WorkingArea;
                int maxW = (int)(wa.Width * Math.Max(10, Math.Min(100, a.Dbl("max_width", 80))) / 100.0);
                Size sz = TextRenderer.MeasureText(text, font, new Size(maxW, 0), TextFormatFlags.WordBreak);
                int pad = Ui.S(Math.Max(0, Math.Min(200, a.Int("padding", 24))));
                var lbl = new Label { Text = text, Font = font, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Col(a.Str("color", "#ffffff"), Color.White), BackColor = Col(a.Str("bg", "#202020"), Color.FromArgb(32, 32, 32)) };
                f.BackColor = lbl.BackColor; f.Padding = new Padding(0);
                f.Controls.Add(lbl);
                int w = sz.Width + pad * 2, h = sz.Height + pad * 2;
                string pos = a.Str("position", "bottom").ToLowerInvariant();
                int x = wa.Left + (wa.Width - w) / 2;
                int mg = a.Has("margin") ? a.Int("margin") : -1;
                int y = pos == "top" ? wa.Top + (mg >= 0 ? mg : 40) : (pos == "center" ? wa.Top + (wa.Height - h) / 2 : wa.Bottom - h - (mg >= 0 ? mg : 60));
                f.Bounds = new Rectangle(x, y, w, h);
                f.Opacity = Math.Max(0.1, Math.Min(1, a.Dbl("opacity", 0.9)));
                f.Slot = a.Str("slot", "");
                ReplaceSlot(f.Slot);
                all.Add(f); f.Show();
                if (!f.ClickThrough) HookClick(f, delegate { f.Close(); });
                double sec = a.Dbl("seconds", 5);
                if (sec > 0)
                {
                    var t = new System.Windows.Forms.Timer { Interval = (int)(sec * 1000) };
                    t.Tick += delegate { t.Stop(); t.Dispose(); if (!f.IsDisposed) f.Close(); };
                    t.Start();
                }
                f.FormClosed += delegate { font.Dispose(); };
                return new Dictionary<string, object> { { "shown", true } };
            });
        }

        // ------------------------------------------------------------ Dialog mit Rückgabewert
        public static object Ask(Args a)
        {
            string text = a.Str("text", a.Str("message", ""));
            var buttons = a.List("buttons"); if (buttons.Count == 0) buttons.Add("OK");
            bool input = a.Bool("input", false);
            int timeout = a.Int("timeout", 0);
            Dictionary<string, object> result = null;
            var done = new ManualResetEvent(false);

            Ui.Send(() =>
            {
                var f = new Form { Text = a.Str("title", "WinCtl"), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = true, TopMost = true, StartPosition = FormStartPosition.Manual, Font = new Font("Segoe UI", (float)Math.Max(6, Math.Min(48, a.Dbl("font_size", 10)))) };
                int W = Ui.S(Math.Max(250, Math.Min(1600, a.Int("width", 460)))), m = Ui.S(20);
                var lbl = new Label { Text = text, AutoSize = false, Left = m, Top = m, Width = W - 2 * m };
                Size sz = TextRenderer.MeasureText(text, f.Font, new Size(lbl.Width, 0), TextFormatFlags.WordBreak);
                lbl.Height = Math.Max(Ui.S(22), sz.Height + Ui.S(4));
                f.Controls.Add(lbl);
                int y = m + lbl.Height + Ui.S(12);
                TextBox tb = null;
                if (input)
                {
                    tb = new TextBox { Left = m, Top = y, Width = W - 2 * m, Text = a.Str("default", ""), UseSystemPasswordChar = a.Bool("password", false) };
                    f.Controls.Add(tb); y += tb.Height + Ui.S(12);
                }
                int n = buttons.Count, gap = Ui.S(8);
                int bh = Ui.S((int)(f.Font.Size * 3.2)), bw = Math.Min(Math.Max(Ui.S(110), (int)(f.Font.Size * Ui.S(11))), (W - 2 * m - gap * (n - 1)) / n);
                int x = (W - (n * bw + (n - 1) * gap)) / 2;
                for (int i = 0; i < n; i++)
                {
                    int idx = i; string label = buttons[i];
                    var b = new Button { Text = label, Left = x + i * (bw + gap), Top = y, Width = bw, Height = bh };
                    b.Click += delegate
                    {
                        if (result == null) result = new Dictionary<string, object> { { "button", label }, { "index", idx }, { "text", tb != null ? tb.Text : null } };
                        f.Close();
                    };
                    f.Controls.Add(b);
                    if (i == 0) f.AcceptButton = b;
                }
                f.ClientSize = new Size(W, y + bh + m);
                Rectangle wa = PickScreen(a.Int("monitor", -1)).WorkingArea;
                f.Location = new Point(wa.Left + (wa.Width - f.Width) / 2, wa.Top + (wa.Height - f.Height) / 2);
                f.FormClosed += delegate
                {
                    if (result == null) result = new Dictionary<string, object> { { "button", null }, { "index", -1 }, { "closed", true } };
                    all.Remove(f); done.Set();
                };
                if (timeout > 0)
                {
                    var t = new System.Windows.Forms.Timer { Interval = timeout * 1000 };
                    t.Tick += delegate { t.Stop(); if (result == null) result = new Dictionary<string, object> { { "button", null }, { "index", -1 }, { "timeout", true } }; f.Close(); };
                    t.Start();
                }
                all.Add(f); f.Show(); f.Activate();
                if (tb != null) tb.Focus();
            });
            done.WaitOne();
            return result;
        }

        public static int CloseAll()
        {
            return Ui.Send<int>(() => { int n = all.Count; foreach (var f in all.ToArray()) { try { f.Close(); } catch { } } all.Clear(); return n; });
        }
    }
}
