using System.Collections.Generic;

namespace WinCtl
{
    /// <summary>
    /// English descriptions for the manifest: ioBroker objects need English texts, the German ones stay in "description_de"
    /// (README.de.md, docs/COMMANDS.md, settings dialog).
    /// </summary>
    public static class EnText
    {
        public static readonly Dictionary<string, string> Commands = new Dictionary<string, string>
        {
            { "ping", "Sign of life, answers with time and agent version" },
            { "get_state", "Publishes all state values again" },
            { "reload_config", "Reloads the agent config and reconnects" },
            { "exit", "Stops the agent" },
            { "list_commands", "Returns the list of all commands" },
            { "check_update", "Checks for a newer agent version (GitHub releases)" },
            { "update", "Installs the newest agent version and restarts the agent" },
            { "lock", "Locks the screen (Win+L)" },
            { "sleep", "Standby" },
            { "hibernate", "Hibernate" },
            { "shutdown", "Shuts the PC down" },
            { "restart", "Restarts the PC" },
            { "logoff", "Logs the user off" },
            { "cancel_shutdown", "Cancels a planned shutdown/restart" },
            { "monitor_off", "Turns the monitors off" },
            { "monitor_on", "Wakes the monitors up" },
            { "screensaver", "Starts the screen saver (the one set in Windows or a .scr file)" },
            { "keep_awake", "Prevents standby and screen saver" },
            { "run", "Starts a program (alias from the agent settings)" },
            { "open", "Opens a URL, file or folder with the default app" },
            { "kill", "Ends processes by name or PID" },
            { "list_processes", "Lists running processes" },
            { "list_windows", "Lists visible windows" },
            { "focus_window", "Brings a window to the front (title contains …)" },
            { "close_window", "Closes a window (title contains …)" },
            { "minimize_all", "Minimizes all windows (show desktop) or restores them" },
            { "empty_recycle_bin", "Empties the recycle bin" },
            { "shell", "Runs a PowerShell/cmd command and returns the output" },
            { "volume", "Sets the master volume (0-100)" },
            { "volume_change", "Changes the volume relatively" },
            { "mute", "Mute / unmute / toggle" },
            { "media", "Sends a media key" },
            { "speak", "Text to speech" },
            { "list_voices", "Lists the installed voices" },
            { "play_sound", "Plays an audio file (mp3/wav/wma, path or URL)" },
            { "stop_sound", "Stops play_sound / speak" },
            { "beep", "Simple beep" },
            { "show_image", "Shows an image as popup, full screen or in a corner" },
            { "show_video", "Plays a video with Edge/Chromium (mp4, WebM, …)" },
            { "show_web", "Shows a web page (vis, Lovelace, camera stream) with Edge/Chromium in a window or full screen, also several pages in turn (slideshow)" },
            { "show_text", "Shows text as an overlay" },
            { "ask", "Shows a dialog with buttons/input and returns the answer" },
            { "notify", "Shows a Windows notification" },
            { "close_popups", "Closes all popups, overlays and dialogs" },
            { "screenshot", "Takes a screenshot (stored in the ioBroker file storage)" },
            { "set_wallpaper", "Sets the desktop wallpaper" },
            { "brightness", "Sets the brightness (internal laptop displays only)" },
            { "send_keys", "Sends key presses (SendKeys syntax)" },
            { "type_text", "Types text into the active window" },
            { "mouse", "Moves/clicks the mouse" },
            { "clipboard_set", "Sets the clipboard text" },
            { "clipboard_get", "Reads the clipboard text" },
            { "start_menu", "Opens or closes the start menu (Windows key)" }
        };

        public static readonly Dictionary<string, string> States = new Dictionary<string, string>
        {
            { "locked", "Screen locked" },
            { "user_present", "User active (not locked and input within the idle threshold)" },
            { "idle", "Seconds since the last mouse/keyboard input" },
            { "active_window", "Title of the active window" },
            { "active_process", "Process of the active window" },
            { "volume", "Master volume" },
            { "muted", "Muted" },
            { "cpu", "CPU load" },
            { "ram", "RAM usage" },
            { "ram_free_gb", "Free memory" },
            { "uptime_min", "Time since Windows start" },
            { "battery", "Battery level (devices with battery only)" },
            { "charging", "Power supply connected (devices with battery only)" },
            { "monitors", "Number of monitors" },
            { "ip", "Local IPv4 address" },
            { "user", "Logged-in user" },
            { "keep_awake", "keep_awake active" },
            { "webcam_in_use", "Camera in use (e.g. video call)" },
            { "webcam_app", "Program(s) using the camera" },
            { "mic_in_use", "Microphone in use" },
            { "mic_app", "Program(s) using the microphone" },
            { "update_available", "Newer agent version available (checked at start and every 12 h)" },
            { "latest_version", "Newest agent version on GitHub" },
            { "disk/<laufwerk>_free_gb", "Free space per drive, e.g. `disk.C_free_gb`" },
            { "disk/<laufwerk>_used_percent", "Usage per drive, e.g. `disk.C_used_percent`" },
            { "proc/<name>", "Watched program is running (agent settings \"Processes\")" },
            { "web_url", "Page that show_web currently displays (empty = none)" }
        };

        public static string Get(Dictionary<string, string> map, string key, string fallback)
        {
            string v;
            return map.TryGetValue(key, out v) ? v : fallback;
        }
    }
}
