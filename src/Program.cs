using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace WinCtl
{
    static class Program
    {
        public const string Version = "0.1.2";

        [STAThread]
        static int Main(string[] args)
        {
            string cfgPath = null; bool headless = false, dump = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--config" && i + 1 < args.Length) cfgPath = args[++i];
                else if (a == "--headless") headless = true;
                else if (a == "--dump-protocol") dump = true;
                else if (a == "--version") { Console.WriteLine("WinCtl " + Version); return 0; }
                else if (a == "--help" || a == "-h" || a == "/?")
                {
                    MessageOrConsole("WinCtl " + Version + "\n\nOptionen:\n  --config <pfad>    Alternative Config-Datei\n  --headless         Ohne GUI/Tray (nur Tests)\n  --dump-protocol    Befehlsliste als JSON ausgeben\n  --version", headless);
                    return 0;
                }
            }
            // always UTF-8, independent of the console code page (otherwise umlauts become "?")
            if (dump) { Console.OutputEncoding = new System.Text.UTF8Encoding(false); Console.WriteLine(Json.Pretty(Json.Dump(new System.Collections.Generic.Dictionary<string, object> { { "protocol", 1 }, { "version", Version }, { "states", StateReporter.Describe() }, { "commands", CmdRegistry.Describe() } }))); return 0; }

            bool first;
            string instance = "WinCtl_SingleInstance_" + (cfgPath ?? "default").GetHashCode();
            var mtx = new Mutex(true, instance, out first);
            // second start: the running instance opens its settings (tray icons are often hidden in the overflow area)
            var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, instance + "_show");
            if (!first && !headless)
            {
                try { Native.AllowSetForegroundWindow(-1); } catch { } // the running instance may bring its window to the front
                var ack = new EventWaitHandle(false, EventResetMode.AutoReset, instance + "_ack");
                showSignal.Set();
                if (!ack.WaitOne(3000))
                    MessageBox.Show("WinCtl läuft bereits, reagiert aber nicht.\n\nBitte im Task-Manager (Reiter „Details“) WinCtl.exe beenden und neu starten.", "WinCtl", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }

            if (!headless) { try { Native.SetProcessDPIAware(); } catch { } }
            if (cfgPath == null) cfgPath = Config.DefaultPath();

            var app = new App(cfgPath, headless);
            bool created = app.LoadConfig();
            if (app.Cfg.LogToFile) Log.Init(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cfgPath)), "winctl.log"));
            Log.Info("WinCtl " + Version + " startet (Config: " + cfgPath + ")");
            Media.CleanupTemp();

            if (headless)
            {
                Console.CancelKeyPress += delegate (object s, ConsoleCancelEventArgs e) { e.Cancel = true; app.Exit(); };
                app.Start();
                app.ExitEvent.WaitOne();
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            BrowserEmulation.Apply();
            Application.ThreadException += (s, e) => Log.Error("UI: " + e.Exception.Message);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("Unbehandelt: " + e.ExceptionObject);
            Ui.Init();
            var ctx = new TrayContext(app);
            var showAck = new EventWaitHandle(false, EventResetMode.AutoReset, instance + "_ack");
            var waiter = new Thread(() => { while (showSignal.WaitOne()) { showAck.Set(); Ui.Post(ctx.ShowSettings); } }) { IsBackground = true, Name = "show-signal" };
            waiter.Start();
            // start in the background so tray and settings appear at once, even if the network or counters are slow
            ThreadPool.QueueUserWorkItem(_ => { try { lock (app.Lifecycle) app.Start(); } catch (Exception ex) { Log.Error("Start: " + ex); } });
            if (created || app.Cfg.Host == "localhost")
            {
                Ui.Post(() =>
                {
                    Ui.Tray.ShowBalloonTip(4000, "WinCtl", "Willkommen! Bitte zuerst den MQTT-Broker eintragen.", ToolTipIcon.Info);
                    ctx.ShowSettings();
                });
            }
            Application.Run(ctx);
            GC.KeepAlive(mtx);
            return 0;
        }

        static void MessageOrConsole(string text, bool console)
        {
            try { MessageBox.Show(text, "WinCtl"); } catch { Console.WriteLine(text); }
        }
    }
}
