namespace TodoWidget.Core;

public enum HotkeyKey
{
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Space, Enter, Tab, Backspace,
    Home, End, PageUp, PageDown, Insert, Delete,
    Up, Down, Left, Right,
}

public sealed record ParsedHotkey(bool Ctrl, bool Alt, bool Shift, bool Win, HotkeyKey Key)
{
    public string ToDisplay()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(DisplayKey(Key));
        return string.Join("+", parts);
    }

    internal static string DisplayKey(HotkeyKey key)
    {
        if (key is >= HotkeyKey.D0 and <= HotkeyKey.D9)
        {
            return ((int)key - (int)HotkeyKey.D0).ToString();
        }
        return key.ToString();
    }
}
