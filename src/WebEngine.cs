using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

namespace WinCtl
{
    /// <summary>
    /// Modern browser for show_web: Microsoft Edge WebView2 (Chromium). The runtime is part of Windows 11 / current Windows 10.
    /// The SDK DLLs are embedded in the exe (gzip resources, see build.sh): the managed ones are loaded from memory,
    /// the native WebView2Loader.dll is unpacked to %LOCALAPPDATA%\WinCtl\webview2\&lt;arch&gt;. Without runtime show_web falls back to IE11.
    /// </summary>
    public static class WebEngine
    {
        static readonly Dictionary<string, string> managed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Microsoft.Web.WebView2.Core", "wv2.core.dll.gz" },
            { "Microsoft.Web.WebView2.WinForms", "wv2.winforms.dll.gz" }
        };
        static bool resolver;
        static string version; static bool probed;

        /// <summary>Loads the embedded WebView2 assemblies on demand (call once at start).</summary>
        public static void InstallResolver()
        {
            if (resolver) return; resolver = true;
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                string res;
                if (!managed.TryGetValue(new AssemblyName(e.Name).Name, out res)) return null;
                byte[] b = Unzip(res);
                return b == null ? null : Assembly.Load(b);
            };
        }

        static byte[] Unzip(string res)
        {
            using (var s = typeof(WebEngine).Assembly.GetManifestResourceStream(res))
            {
                if (s == null) return null;
                using (var z = new GZipStream(s, CompressionMode.Decompress))
                using (var m = new MemoryStream()) { z.CopyTo(m); return m.ToArray(); }
            }
        }

        public static string DataDir(string sub)
        {
            string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinCtl", sub);
            Directory.CreateDirectory(d);
            return d;
        }

        /// <summary>Folder with the native WebView2Loader.dll for this process architecture (unpacked once, rewritten when it changed).</summary>
        public static string LoaderFolder()
        {
            string arch = Environment.Is64BitProcess ? (string.Equals(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE"), "ARM64", StringComparison.OrdinalIgnoreCase) ? "arm64" : "x64") : "x86";
            byte[] b = Unzip("wv2.loader." + arch + ".dll.gz");
            if (b == null) throw new InvalidOperationException("WebView2Loader für " + arch + " fehlt in der exe");
            string dir = DataDir(Path.Combine("webview2", arch)), file = Path.Combine(dir, "WebView2Loader.dll");
            if (!File.Exists(file) || new FileInfo(file).Length != b.Length) File.WriteAllBytes(file, b);
            return dir;
        }

        /// <summary>Installed WebView2 runtime version, null if it is missing (then IE11 is used).</summary>
        public static string Version()
        {
            if (probed) return version;
            probed = true;
            // resolver first: the JIT loads the WebView2 assemblies as soon as it compiles Edge.Probe
            try { InstallResolver(); version = Edge.Probe(); }
            catch (Exception ex) { version = null; Log.Info("WebView2 nicht verfügbar, show_web nutzt IE11: " + ex.Message); }
            return version;
        }
    }

    /// <summary>Everything that touches WebView2 types; only called after WebEngine.Version() found the runtime.</summary>
    static class Edge
    {
        public static string Probe()
        {
            Microsoft.Web.WebView2.Core.CoreWebView2Environment.SetLoaderDllFolderPath(WebEngine.LoaderFolder());
            return Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
        }

        /// <summary>WebView2 control that shows url; onUrl gets every page that finished loading.</summary>
        public static Control Create(string url, double zoom, bool insecureTls, Action<string> onUrl)
        {
            var wv = new Microsoft.Web.WebView2.WinForms.WebView2
            {
                // same user data folder needs the same browser arguments, so one folder per setting
                CreationProperties = new Microsoft.Web.WebView2.WinForms.CoreWebView2CreationProperties
                {
                    UserDataFolder = WebEngine.DataDir(insecureTls ? "webview2-data-insecure" : "webview2-data"),
                    AdditionalBrowserArguments = insecureTls ? "--ignore-certificate-errors" : null
                },
                DefaultBackgroundColor = System.Drawing.Color.Black
            };
            wv.CoreWebView2InitializationCompleted += (s, e) =>
            {
                if (!e.IsSuccess) { Log.Error("WebView2 konnte nicht starten: " + (e.InitializationException != null ? e.InitializationException.Message : "?")); return; }
                wv.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                wv.CoreWebView2.Settings.AreDevToolsEnabled = false;
                wv.CoreWebView2.Settings.IsStatusBarEnabled = false;
                wv.CoreWebView2.NavigationCompleted += delegate { if (onUrl != null) onUrl(wv.Source.AbsoluteUri); };
                wv.ZoomFactor = zoom;
            };
            wv.Source = new Uri(url);
            return wv;
        }

        public static void Navigate(Control c, string url) { ((Microsoft.Web.WebView2.WinForms.WebView2)c).Source = new Uri(url); }
    }
}
