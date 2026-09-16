using System.Windows;
using TodoWidget.Core;

namespace TodoWidget.Desktop.Interop;

internal static class HotkeyKeyMapper
{
    public static uint Modifiers(ParsedHotkey hotkey)
    {
        uint flags = 0;
        if (hotkey.Alt) flags |= NativeMethods.MOD_ALT;
        if (hotkey.Ctrl) flags |= NativeMethods.MOD_CONTROL;
        if (hotkey.Shift) flags |= NativeMethods.MOD_SHIFT;
        if (hotkey.Win) flags |= NativeMethods.MOD_WIN;
        return flags;
    }

    public static uint VirtualKey(HotkeyKey key)
    {
        if (key is >= HotkeyKey.A and <= HotkeyKey.Z)
            return (uint)('A' + (key - HotkeyKey.A));
        if (key is >= HotkeyKey.D0 and <= HotkeyKey.D9)
            return (uint)('0' + (key - HotkeyKey.D0));
        if (key is >= HotkeyKey.F1 and <= HotkeyKey.F12)
            return 0x70u + (uint)(key - HotkeyKey.F1);
        return key switch
        {
            HotkeyKey.Space => 0x20,
            HotkeyKey.Enter => 0x0D,
            HotkeyKey.Tab => 0x09,
            HotkeyKey.Backspace => 0x08,
            HotkeyKey.Home => 0x24,
            HotkeyKey.End => 0x23,
            HotkeyKey.PageUp => 0x21,
            HotkeyKey.PageDown => 0x22,
            HotkeyKey.Insert => 0x2D,
            HotkeyKey.Delete => 0x2E,
            HotkeyKey.Up => 0x26,
            HotkeyKey.Down => 0x28,
            HotkeyKey.Left => 0x25,
            HotkeyKey.Right => 0x27,
            _ => 0,
        };
    }
}
