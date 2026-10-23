using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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

        public void ShowSettings()
        {
            using (var f = new SettingsForm(app)) f.ShowDialog();
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

    public class SettingsForm : Form
    {
        readonly App app;
        TextBox host, user, pass, baseTopic, devId;
        NumericUpDown port;
        CheckBox tls, auto, anyRun, shell;

        public SettingsForm(App a)
        {
            app = a; var c = a.Cfg;
            Text = "WinCtl - Einstellungen"; Font = new Font("Segoe UI", 9.5f); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen; TopMost = true;
            int W = Ui.S(440), lx = Ui.S(16), fx = Ui.S(150), fw = W - fx - lx, y = Ui.S(16), step = Ui.S(32);

            Action<string, Control> row = (label, ctl) =>
            {
                Controls.Add(new Label { Text = label, Left = lx, Top = y + Ui.S(3), Width = fx - lx - Ui.S(4) });
                ctl.Left = fx; ctl.Top = y; ctl.Width = fw; Controls.Add(ctl); y += step;
            };
            host = new TextBox { Text = c.Host }; row("MQTT-Broker", host);
            port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Math.Max(1, Math.Min(65535, c.Port)) }; row("Port", port);
            user = new TextBox { Text = c.Username }; row("Benutzer", user);
            pass = new TextBox { Text = c.Password, UseSystemPasswordChar = true }; row("Passwort", pass);
            tls = new CheckBox { Text = "TLS (verschlüsselt)", Checked = c.Tls }; row("", tls);
            baseTopic = new TextBox { Text = c.BaseTopic }; row("Topic-Basis", baseTopic);
            devId = new TextBox { Text = c.DeviceId }; row("Geräte-ID (leer = " + Environment.MachineName.ToLowerInvariant() + ")", devId);
            auto = new CheckBox { Text = "Mit Windows starten", Checked = Autostart.IsOn() }; row("", auto);
            anyRun = new CheckBox { Text = "run/open: beliebige Programme erlauben", Checked = c.AllowAnyRun }; row("Sicherheit", anyRun);
            shell = new CheckBox { Text = "shell: PowerShell/cmd-Befehle erlauben", Checked = c.AllowShell }; row("", shell);
            anyRun.Width = fw + Ui.S(40); shell.Width = fw + Ui.S(40);

            var ok = new Button { Text = "Speichern", Left = W - Ui.S(230), Top = y + Ui.S(6), Width = Ui.S(100), Height = Ui.S(30) };
            var cancel = new Button { Text = "Abbrechen", Left = W - Ui.S(120), Top = y + Ui.S(6), Width = Ui.S(100), Height = Ui.S(30), DialogResult = DialogResult.Cancel };
            ok.Click += delegate { Save(); };
            Controls.Add(ok); Controls.Add(cancel); AcceptButton = ok; CancelButton = cancel;
            ClientSize = new Size(W, y + Ui.S(52));
        }

        void Save()
        {
            var c = app.Cfg;
            c.Host = host.Text.Trim(); c.Port = (int)port.Value; c.Username = user.Text; c.Password = pass.Text; c.Tls = tls.Checked;
            c.BaseTopic = string.IsNullOrWhiteSpace(baseTopic.Text) ? "winctl" : baseTopic.Text.Trim().Trim('/');
            c.DeviceId = devId.Text.Trim(); c.AllowAnyRun = anyRun.Checked; c.AllowShell = shell.Checked;
            try { c.Save(app.CfgPath); } catch (Exception ex) { MessageBox.Show("Config konnte nicht gespeichert werden:\n" + ex.Message, "WinCtl", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
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
