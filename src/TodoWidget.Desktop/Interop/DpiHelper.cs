using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TodoWidget.Desktop.Interop;

internal static class DpiHelper
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public static double GetWindowDpiScale(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return 1.0;
        uint dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }
}
