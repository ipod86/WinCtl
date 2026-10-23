using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace WinCtl
{
    public class WinInfo { public IntPtr Handle; public string Title; public uint Pid; }

    public static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool LockWorkStation();
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(uint action, uint param, string vparam, uint winIni);
        [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO p);
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
        [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);
        [DllImport("powrprof.dll")] public static extern bool SetSuspendState(bool hibernate, bool force, bool wakeupDisabled);
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] public static extern int mciSendString(string cmd, StringBuilder ret, int retLen, IntPtr cb);
        [DllImport("shell32.dll")] public static extern int SHEmptyRecycleBin(IntPtr h, string root, uint flags);

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        class MEMORYSTATUSEX
        {
            public uint dwLength; public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
            public MEMORYSTATUSEX() { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)); }
        }

        public const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2;

        public static int IdleSeconds()
        {
            var i = new LASTINPUTINFO(); i.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref i)) return 0;
            uint now = unchecked((uint)Environment.TickCount);
            return (int)(unchecked(now - i.dwTime) / 1000);
        }

        public static long UptimeSeconds() { return (long)(GetTickCount64() / 1000); }

        public static void RamInfo(out double usedPercent, out double totalGb, out double freeGb)
        {
            var m = new MEMORYSTATUSEX(); GlobalMemoryStatusEx(m);
            usedPercent = m.dwMemoryLoad; totalGb = m.ullTotalPhys / 1073741824.0; freeGb = m.ullAvailPhys / 1073741824.0;
        }

        public static string WindowTitle(IntPtr h)
        {
            int n = GetWindowTextLength(h); if (n <= 0) return "";
            var sb = new StringBuilder(n + 2); GetWindowText(h, sb, sb.Capacity); return sb.ToString();
        }

        public static List<WinInfo> Windows(bool onlyTitled = true)
        {
            var list = new List<WinInfo>();
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h)) return true;
                string t = WindowTitle(h);
                if (onlyTitled && t.Length == 0) return true;
                uint pid; GetWindowThreadProcessId(h, out pid);
                list.Add(new WinInfo { Handle = h, Title = t, Pid = pid });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static void MonitorPower(bool on)
        {
            IntPtr res;
            SendMessageTimeout(new IntPtr(0xFFFF), 0x0112, new IntPtr(0xF170), new IntPtr(on ? -1 : 2), 2, 1000, out res);
            if (on) mouse_event(0x0001, 1, 0, 0, UIntPtr.Zero);
        }

        public static void Key(byte vk, bool extended = false)
        {
            keybd_event(vk, 0, extended ? KEYEVENTF_EXTENDEDKEY : 0, UIntPtr.Zero);
            keybd_event(vk, 0, (extended ? KEYEVENTF_EXTENDEDKEY : 0) | KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }
}
