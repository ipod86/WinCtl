using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinCtl
{
    public static class Autostart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool IsOn()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(Key)) return k != null && k.GetValue("WinCtl") != null; } catch { return false; }
        }
        public static void Set(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Key))
                {
                    if (on) k.SetValue("WinCtl", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("WinCtl", false);
                }
            }
            catch (Exception ex) { Log.Error("Autostart: " + ex.Message); }
        }
    }

    public static class BrowserEmulation
    {
        /// <summary>Das WebBrowser-Control läuft sonst im IE7-Modus - hier auf IE11 stellen (HTML5-Video, moderne Seiten).</summary>
        public static void Apply()
        {
            try
            {
                string exe = Path.GetFileName(Application.ExecutablePath);
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION"))
                    k.SetValue(exe, 11001, RegistryValueKind.DWord);
            }
            catch { }
        }
    }

    public class TrayContext : ApplicationContext
    {
        readonly App app;
        readonly NotifyIcon tray;
        readonly ToolStripMenuItem status, autostart;
        Icon iconOn, iconOff;

        public TrayContext(App a)
        {
            app = a;
            iconOn = MakeIcon(true); iconOff = MakeIcon(false);
            status = new ToolStripMenuItem("Verbinde ...") { Enabled = false };
            autostart = new ToolStripMenuItem("Mit Windows starten") { Checked = Autostart.IsOn(), CheckOnClick = true };
            autostart.CheckedChanged += delegate { Autostart.Set(autostart.Checked); };

            var menu = new ContextMenuStrip();
            menu.Items.Add(status);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Einstellungen ...", null, delegate { ShowSettings(); });
            menu.Items.Add("Log anzeigen ...", null, delegate { new LogForm().Show(); });
            menu.Items.Add("Test: Text-Overlay", null, delegate { try { Popups.ShowText(new Args(new Dictionary<string, object> { { "text", "WinCtl läuft \u2713" }, { "seconds", 3 } })); } catch (Exception ex) { Log.Error(ex.Message); } });
            menu.Items.Add("Config-Datei öffnen", null, delegate { try { Process.Start("notepad.exe", "\"" + app.CfgPath + "\""); } catch { } });
            menu.Items.Add("Config neu laden", null, delegate { app.Reload(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(autostart);
            menu.Items.Add("Nach Update suchen ...", null, delegate { Updater.CheckInteractive(app, false); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Beenden", null, delegate { app.Exit(); });

            tray = new NotifyIcon { Icon = iconOff, ContextMenuStrip = menu, Visible = true, Text = "WinCtl" };
            tray.DoubleClick += delegate { ShowSettings(); };
            Ui.Tray = tray;

            app.ConnectionChanged += OnConn;
            app.ConfigReloaded += delegate { UpdateStatus(app.Mqtt != null && app.Mqtt.IsConnected); };
            UpdateStatus(false);
        }

        void OnConn(bool up) { Ui.Post(() => UpdateStatus(up)); }

        void UpdateStatus(bool up)
        {
            string t = (up ? "Verbunden: " : "Getrennt: ") + app.Cfg.Host + ":" + app.Cfg.Port + "  (" + app.Id + ")";
            status.Text = t;
            tray.Icon = up ? iconOn : iconOff;
            tray.Text = ("WinCtl - " + (up ? "verbunden" : "getrennt")).Substring(0, Math.Min(63, ("WinCtl - " + (up ? "verbunden" : "getrennt")).Length));
        }

        SettingsForm settings;

        /// <summary>Öffnet den Einstellungsdialog bzw. holt ihn nach vorn, wenn er schon offen ist</summary>
        public void ShowSettings()
        {
            if (settings != null) { settings.WindowState = FormWindowState.Normal; settings.Activate(); return; }
            try
            {
                using (settings = new SettingsForm(app))
                {
                    settings.Shown += delegate { settings.Activate(); };
                    settings.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Einstellungen: " + ex);
                Ui.Msg("Der Einstellungsdialog konnte nicht geöffnet werden:\n" + ex.Message + "\n\nDetails stehen in winctl.log.", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { settings = null; }
        }

        static Icon MakeIcon(bool on)
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 32, 32), Color.FromArgb(45, 125, 255), Color.FromArgb(20, 60, 170), 90f))
                        g.FillEllipse(br, 1, 1, 30, 30);
                    using (var f = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString("W", f, Brushes.White, new RectangleF(0, 1, 32, 32), sf);
                    using (var br = new SolidBrush(on ? Color.FromArgb(40, 200, 90) : Color.FromArgb(220, 60, 60)))
                    { g.FillEllipse(Brushes.White, 19, 19, 13, 13); g.FillEllipse(br, 21, 21, 9, 9); }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { tray.Visible = false; tray.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /// <summary>Einstellungsdialog mit Reitern - deckt die komplette Config ab (die JSON-Datei muss niemand von Hand bearbeiten)</summary>
    public class SettingsForm : Form
    {
        readonly App app;
        TextBox host, user, pass, baseTopic, devId;
        NumericUpDown port, interval, idle;
        CheckBox tls, tlsInvalid, auto, anyRun, shell, mediaTls, logFile, updCheck;
        DataGridView apps, hotkeys, procs;
        CheckedListBox disabled;
        readonly List<string> cmdNames = new List<string>();

        public SettingsForm(App a)
        {
            app = a; var c = a.Cfg;
            Text = "WinCtl - Einstellungen"; Font = new Font("Segoe UI", 9.5f); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen; TopMost = true;
            int W = Ui.S(600), H = Ui.S(470);
            ClientSize = new Size(W, H);

            var tabs = new TabControl { Left = Ui.S(8), Top = Ui.S(8), Width = W - Ui.S(16), Height = H - Ui.S(62) };
            Controls.Add(tabs);

            // --- Verbindung ---
            var tp = Page(tabs, "Verbindung"); var row = Rows(tp);
            host = new TextBox { Text = c.Host }; row("MQTT-Broker", host);
            port = Num(1, 65535, c.Port); row("Port", port);
            user = new TextBox { Text = c.Username }; row("Benutzer", user);
            pass = new TextBox { Text = c.Password, UseSystemPasswordChar = true }; row("Passwort", pass);
            tls = new CheckBox { Text = "TLS (verschlüsselt)", Checked = c.Tls }; row("", tls);
            tlsInvalid = new CheckBox { Text = "Selbstsigniertes Broker-Zertifikat erlauben", Checked = c.TlsAllowInvalidCert }; row("", tlsInvalid);
            baseTopic = new TextBox { Text = c.BaseTopic }; row("Topic-Basis", baseTopic);
            devId = new TextBox { Text = c.DeviceId }; row("Geräte-ID", devId);
            Hint(tp, devId, "leer = " + Environment.MachineName.ToLowerInvariant());

            // --- Allgemein ---
            tp = Page(tabs, "Allgemein"); row = Rows(tp);
            auto = new CheckBox { Text = "Mit Windows starten", Checked = Autostart.IsOn() }; row("", auto);
            interval = Num(2, 3600, c.StateIntervalSec); row("Werte senden alle (s)", interval);
            idle = Num(10, 86400, c.IdleThresholdSec); row("Abwesend nach (s)", idle);
            Hint(tp, idle, "ohne Maus/Tastatur");
            mediaTls = new CheckBox { Text = "Selbstsignierte https-Quellen für Bilder/Videos erlauben (z. B. Kameras)", Checked = c.AllowInsecureMediaTls }; row("", mediaTls);
            logFile = new CheckBox { Text = "Log zusätzlich in Datei schreiben", Checked = c.LogToFile }; row("", logFile);
            updCheck = new CheckBox { Text = "Beim Start nach Updates suchen (Hinweis im Tray)", Checked = c.CheckForUpdates }; row("", updCheck);

            // --- Programme ---
            tp = Page(tabs, "Programme");
            apps = Grid(tp, "Alias", "Programm / Pfad", true);
            foreach (var kv in c.Apps) apps.Rows.Add(kv.Key, kv.Value);
            apps.CellContentClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 2) return;
                using (var d = new OpenFileDialog { Filter = "Programme (*.exe;*.bat;*.cmd;*.lnk)|*.exe;*.bat;*.cmd;*.lnk|Alle Dateien (*.*)|*.*" })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    if (e.RowIndex == apps.NewRowIndex) apps.Rows.Add();
                    var r = apps.Rows[e.RowIndex];
                    r.Cells[1].Value = d.FileName;
                    if (string.IsNullOrWhiteSpace(Str(r.Cells[0].Value))) r.Cells[0].Value = Path.GetFileNameWithoutExtension(d.FileName).ToLowerInvariant();
                }
            };
            anyRun = new CheckBox { Text = "run/open: auch beliebige Programme erlauben (nicht nur diese Aliase)", Checked = c.AllowAnyRun, Dock = DockStyle.Bottom, Height = Ui.S(28) };
            tp.Controls.Add(anyRun);

            // --- Hotkeys ---
            tp = Page(tabs, "Hotkeys");
            hotkeys = Grid(tp, "Tastenkürzel (anklicken + drücken)", "Event-Name (an ioBroker)", false);
            foreach (var kv in c.Hotkeys) hotkeys.Rows.Add(kv.Key, kv.Value);
            hotkeys.EditingControlShowing += (s, e) =>
            {
                var tb = e.Control as TextBox; if (tb == null) return;
                tb.KeyDown -= CaptureHotkey; tb.ReadOnly = false;
                if (hotkeys.CurrentCell.ColumnIndex == 0) { tb.KeyDown += CaptureHotkey; tb.ReadOnly = true; }
            };

            // --- Prozesse ---
            tp = Page(tabs, "Prozesse");
            procs = Grid(tp, "Prozessname ohne .exe (-> state/proc/<name>)", null, false);
            foreach (var pn in c.WatchProcesses) procs.Rows.Add(pn);
            var pick = new Button { Text = "Laufenden Prozess hinzufügen ...", Dock = DockStyle.Bottom, Height = Ui.S(30) };
            pick.Click += delegate { PickProcess(); };
            tp.Controls.Add(pick);

            // --- Befehle ---
            tp = Page(tabs, "Befehle");
            shell = new CheckBox { Text = "shell: PowerShell/cmd-Befehle erlauben", Checked = c.AllowShell, Dock = DockStyle.Top, Height = Ui.S(28) };
            var info = new Label { Text = "Angehakte Befehle sind abgeschaltet (auch nicht per ioBroker aufrufbar):", Dock = DockStyle.Top, Height = Ui.S(24) };
            disabled = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
            var off = new HashSet<string>(c.DisabledCommands ?? new string[0], StringComparer.OrdinalIgnoreCase);
            foreach (var d in CmdRegistry.All().OrderBy(x => x.Category).ThenBy(x => x.Name))
            {
                cmdNames.Add(d.Name);
                disabled.Items.Add(d.Name + (d.Dangerous ? " (!)" : "") + "  -  " + d.Desc, off.Contains(d.Name));
            }
            tp.Controls.Add(disabled); tp.Controls.Add(info); tp.Controls.Add(shell);

            var ok = new Button { Text = "Speichern", Left = W - Ui.S(230), Top = H - Ui.S(44), Width = Ui.S(100), Height = Ui.S(30) };
            var cancel = new Button { Text = "Abbrechen", Left = W - Ui.S(120), Top = H - Ui.S(44), Width = Ui.S(100), Height = Ui.S(30), DialogResult = DialogResult.Cancel };
            var file = new LinkLabel { Text = "Config-Datei: " + Path.GetFileName(app.CfgPath), Left = Ui.S(12), Top = H - Ui.S(38), AutoSize = true };
            file.LinkClicked += delegate { try { Process.Start("explorer.exe", "/select,\"" + app.CfgPath + "\""); } catch { } };
            ok.Click += delegate { Save(); };
            Controls.Add(ok); Controls.Add(cancel); Controls.Add(file); AcceptButton = ok; CancelButton = cancel;
        }

        static TabPage Page(TabControl tabs, string title)
        {
            var p = new TabPage(title) { Padding = new Padding(Ui.S(8)), UseVisualStyleBackColor = true };
            tabs.TabPages.Add(p); return p;
        }

        static Action<string, Control> Rows(TabPage p)
        {
            int lx = Ui.S(8), fx = Ui.S(170), y = Ui.S(12), step = Ui.S(34);
            return (label, ctl) =>
            {
                if (label.Length > 0) p.Controls.Add(new Label { Text = label, Left = lx, Top = y + Ui.S(3), Width = fx - lx - Ui.S(4) });
                ctl.Left = label.Length > 0 ? fx : lx; ctl.Top = y;
                ctl.Width = (ctl is CheckBox) ? Ui.S(540) - ctl.Left : Ui.S(260);
                p.Controls.Add(ctl); y += step;
            };
        }

        static void Hint(TabPage p, Control next, string text)
        {
            p.Controls.Add(new Label { Text = text, Left = next.Right + Ui.S(8), Top = next.Top + Ui.S(3), AutoSize = true, ForeColor = SystemColors.GrayText });
        }

        static NumericUpDown Num(int min, int max, int val) { return new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, val)) }; }

        static DataGridView Grid(TabPage p, string col1, string col2, bool browse)
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersWidth = Ui.S(28),
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle
            };
            g.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = col1, FillWeight = col2 == null ? 100 : 35 });
            if (col2 != null) g.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = col2, FillWeight = 65 });
            if (browse) g.Columns.Add(new DataGridViewButtonColumn { HeaderText = "", Text = "...", UseColumnTextForButtonValue = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = Ui.S(36) });
            p.Controls.Add(g);
            var hint = new Label { Text = "Neue Zeile: unten eintragen. Löschen: Zeile links markieren + Entf.", Dock = DockStyle.Bottom, Height = Ui.S(22), ForeColor = SystemColors.GrayText };
            p.Controls.Add(hint);
            return g;
        }

        /// <summary>Tastenkombination direkt aufnehmen statt tippen (Format wie in HotkeyManager.TryParse)</summary>
        void CaptureHotkey(object sender, KeyEventArgs e)
        {
            var tb = (TextBox)sender; var k = e.KeyCode;
            if (k == Keys.Tab || k == Keys.Enter || k == Keys.Escape) return;
            e.SuppressKeyPress = true; e.Handled = true;
            if (k == Keys.Back || k == Keys.Delete) { tb.Text = ""; return; }
            if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin) return;
            var parts = new List<string>();
            if (e.Control) parts.Add("ctrl");
            if (e.Alt) parts.Add("alt");
            if (e.Shift) parts.Add("shift");
            if ((Native.GetAsyncKeyState((int)Keys.LWin) & 0x8000) != 0 || (Native.GetAsyncKeyState((int)Keys.RWin) & 0x8000) != 0) parts.Add("win");
            parts.Add(k >= Keys.D0 && k <= Keys.D9 ? ((int)(k - Keys.D0)).ToString() : k.ToString().ToLowerInvariant());
            tb.Text = string.Join("+", parts);
        }

        void PickProcess()
        {
            var names = Process.GetProcesses().Select(p => { try { return p.ProcessName; } catch { return null; } })
                .Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            using (var f = new Form { Text = "Prozess wählen", Size = new Size(Ui.S(320), Ui.S(420)), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.SizableToolWindow, TopMost = true })
            {
                var lb = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false }; lb.Items.AddRange(names);
                var b = new Button { Text = "Hinzufügen", Dock = DockStyle.Bottom, Height = Ui.S(30), DialogResult = DialogResult.OK };
                lb.DoubleClick += delegate { f.DialogResult = DialogResult.OK; };
                f.Controls.Add(lb); f.Controls.Add(b); f.AcceptButton = b;
                if (f.ShowDialog(this) == DialogResult.OK && lb.SelectedItem != null) procs.Rows.Add((string)lb.SelectedItem);
            }
        }

        static string Str(object o) { return o == null ? "" : o.ToString().Trim(); }

        /// <summary>Zweispaltiges Grid -> Dictionary; Fehlermeldung bei leeren/doppelten Schlüsseln</summary>
        Dictionary<string, string> ReadPairs(DataGridView g, string what, Func<string, string> check, out string error)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); error = null;
            foreach (DataGridViewRow r in g.Rows)
            {
                if (r.IsNewRow) continue;
                string k = Str(r.Cells[0].Value), v = Str(r.Cells[1].Value);
                if (k.Length == 0 && v.Length == 0) continue;
                if (k.Length == 0 || v.Length == 0) { error = what + ": Zeile unvollständig (" + k + v + ")"; return null; }
                string err = check != null ? check(k) : null;
                if (err != null) { error = what + ": " + err; return null; }
                if (d.ContainsKey(k)) { error = what + ": \"" + k + "\" ist doppelt"; return null; }
                d[k] = v;
            }
            return d;
        }

        void Save()
        {
            string err;
            var appMap = ReadPairs(apps, "Programme", null, out err);
            Dictionary<string, string> hotMap = null;
            if (err == null) hotMap = ReadPairs(hotkeys, "Hotkeys", k => { uint m; Keys key; return HotkeyManager.TryParse(k, out m, out key) ? null : "\"" + k + "\" ist kein gültiges Tastenkürzel"; }, out err);
            if (err != null) { MessageBox.Show(this, err, "WinCtl", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            var c = app.Cfg;
            c.Host = host.Text.Trim(); c.Port = (int)port.Value; c.Username = user.Text; c.Password = pass.Text;
            c.Tls = tls.Checked; c.TlsAllowInvalidCert = tlsInvalid.Checked;
            c.BaseTopic = string.IsNullOrWhiteSpace(baseTopic.Text) ? "winctl" : baseTopic.Text.Trim().Trim('/');
            c.DeviceId = devId.Text.Trim();
            c.StateIntervalSec = (int)interval.Value; c.IdleThresholdSec = (int)idle.Value;
            c.AllowInsecureMediaTls = mediaTls.Checked; c.LogToFile = logFile.Checked; c.CheckForUpdates = updCheck.Checked;
            c.AllowAnyRun = anyRun.Checked; c.AllowShell = shell.Checked;
            c.Apps = appMap; c.Hotkeys = hotMap;
            c.WatchProcesses = procs.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow)
                .Select(r => Regex.Replace(Str(r.Cells[0].Value), @"\.exe$", "", RegexOptions.IgnoreCase))
                .Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            c.DisabledCommands = disabled.CheckedIndices.Cast<int>().Select(i => cmdNames[i]).ToArray();
            try { c.Save(app.CfgPath); } catch (Exception ex) { MessageBox.Show(this, "Config konnte nicht gespeichert werden:\n" + ex.Message, "WinCtl", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            Autostart.Set(auto.Checked);
            DialogResult = DialogResult.OK; Close();
            System.Threading.ThreadPool.QueueUserWorkItem(_ => app.Restart());
        }
    }

    public class LogForm : Form
    {
        readonly TextBox tb = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f) };
        readonly Action<string> handler;
        public LogForm()
        {
            Text = "WinCtl - Log"; Size = new Size(Ui.S(900), Ui.S(500)); StartPosition = FormStartPosition.CenterScreen;
            Controls.Add(tb); tb.Text = Log.All(); tb.SelectionStart = tb.TextLength; tb.ScrollToCaret();
            handler = line => Ui.Post(() => { if (!IsDisposed) { tb.AppendText(line + Environment.NewLine); } });
            Log.Added += handler;
            FormClosed += delegate { Log.Added -= handler; };
        }
    }
}
