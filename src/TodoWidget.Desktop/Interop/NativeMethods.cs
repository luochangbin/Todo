using System.Runtime.InteropServices;
using TodoWidget.Core;

namespace TodoWidget.Desktop.Interop;

internal static class NativeMethods
{
    public const int WM_HOTKEY = 0x0312;

    public const uint MOD_ALT = 0x1;
    public const uint MOD_CONTROL = 0x2;
    public const uint MOD_SHIFT = 0x4;
    public const uint MOD_WIN = 0x8;

    public const uint MONITOR_DEFAULTTONEAREST = 0x2;
    public const uint MONITORINFOF_PRIMARY = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;

        public static MONITORINFO Create() => new() { cbSize = Marshal.SizeOf<MONITORINFO>() };
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    public sealed class MonitorInfo
    {
        public required RECT Work;
        public required RECT Monitor;
        public bool IsPrimary;
    }

    public static List<MonitorInfo> GetAllMonitors()
    {
        var list = new List<MonitorInfo>();
        MonitorEnumProc callback = (hMonitor, _, ref _, _) =>
        {
            var info = MONITORINFO.Create();
            if (GetMonitorInfoW(hMonitor, ref info))
            {
                list.Add(new MonitorInfo
                {
                    Work = info.rcWork,
                    Monitor = info.rcMonitor,
                    IsPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                });
            }
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return list;
    }

    public static MonitorInfo? MonitorInfoUnder(RectD dipRect, double scale)
    {
        var center = new POINT
        {
            X = (int)Math.Round((dipRect.Left + dipRect.Width / 2) * scale),
            Y = (int)Math.Round((dipRect.Top + dipRect.Height / 2) * scale),
        };
        var monitor = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
        var info = MONITORINFO.Create();
        if (GetMonitorInfoW(monitor, ref info))
        {
            return new MonitorInfo
            {
                Work = info.rcWork,
                Monitor = info.rcMonitor,
                IsPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
            };
        }
        return null;
    }

    public static RECT GetWindowRectDpiAware(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var rect);
        return rect;
    }
}
