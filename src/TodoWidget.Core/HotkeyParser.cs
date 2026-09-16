namespace TodoWidget.Core;

public static class HotkeyParser
{
    public static bool TryParse(string text, out ParsedHotkey result, out string error)
    {
        result = null!;
        error = "";

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "快捷键不能为空";
            return false;
        }

        bool ctrl = false, alt = false, shift = false, win = false;
        HotkeyKey? key = null;
        int keyTokenCount = 0;

        var tokens = text.Split('+');
        foreach (var raw in tokens)
        {
            var token = raw.Trim();
            if (token.Length == 0)
            {
                error = "格式无效：修饰键与按键之间以 + 连接";
                return false;
            }

            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    if (ctrl) return Duplicate("Ctrl", out result, out error);
                    ctrl = true;
                    break;
                case "alt":
                    if (alt) return Duplicate("Alt", out result, out error);
                    alt = true;
                    break;
                case "shift":
                    if (shift) return Duplicate("Shift", out result, out error);
                    shift = true;
                    break;
                case "win":
                case "windows":
                    if (win) return Duplicate("Win", out result, out error);
                    win = true;
                    break;
                default:
                    keyTokenCount++;
                    if (keyTokenCount > 1)
                    {
                        error = "格式无效：只允许一个主键";
                        return false;
                    }
                    key = ParseKeyToken(token);
                    if (key is null)
                    {
                        error = $"格式无效：无法识别的按键 \"{token}\"";
                        return false;
                    }
                    break;
            }
        }

        if (!(ctrl || alt || shift || win))
        {
            error = "格式无效：必须包含至少一个修饰键 (Ctrl/Alt/Shift/Win)";
            return false;
        }
        if (key is null)
        {
            error = "格式无效：缺少主键";
            return false;
        }

        result = new ParsedHotkey(ctrl, alt, shift, win, key.Value);
        return true;
    }

    private static bool Duplicate(string name, out ParsedHotkey result, out string error)
    {
        result = null!;
        error = $"格式无效：重复的修饰键 {name}";
        return false;
    }

    private static HotkeyKey? ParseKeyToken(string token)
    {
        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z') return (HotkeyKey)(c - 'A');
            if (c is >= '0' and <= '9') return HotkeyKey.D0 + (c - '0');
            return null;
        }

        switch (token.ToLowerInvariant())
        {
            case "space": return HotkeyKey.Space;
            case "enter": return HotkeyKey.Enter;
            case "tab": return HotkeyKey.Tab;
            case "backspace": return HotkeyKey.Backspace;
            case "home": return HotkeyKey.Home;
            case "end": return HotkeyKey.End;
            case "pageup": return HotkeyKey.PageUp;
            case "pagedown": return HotkeyKey.PageDown;
            case "insert": return HotkeyKey.Insert;
            case "delete": return HotkeyKey.Delete;
            case "up": return HotkeyKey.Up;
            case "down": return HotkeyKey.Down;
            case "left": return HotkeyKey.Left;
            case "right": return HotkeyKey.Right;
        }

        if (token.Length == 2 && (token[0] == 'f' || token[0] == 'F')
            && token[1] is >= '1' and <= '9')
        {
            return HotkeyKey.F1 + (token[1] - '1');
        }
        if (token.Equals("F10", StringComparison.OrdinalIgnoreCase)
            || token.Equals("F11", StringComparison.OrdinalIgnoreCase)
            || token.Equals("F12", StringComparison.OrdinalIgnoreCase))
        {
            return token.ToUpperInvariant() switch
            {
                "F10" => HotkeyKey.F10,
                "F11" => HotkeyKey.F11,
                _ => HotkeyKey.F12,
            };
        }

        return null;
    }
}

public static class AppHotkeyRules
{
    /// <summary>校验单一全局快捷键；留空表示禁用，返回 null。</summary>
    public static string? ValidateToggleHotkey(string toggleHotkey)
    {
        if (string.IsNullOrWhiteSpace(toggleHotkey)) return null;
        return HotkeyParser.TryParse(toggleHotkey, out _, out var error) ? null : error;
    }

    /// <summary>
    /// 按下全局快捷键时应"唤出并激活"还是"最小化"。
    /// 已隐藏/最小化 → 唤出；显示中但不在前台 → 先唤到最前。
    /// 只有它已经是当前窗口时才最小化，否则"把别的程序切到前面"之后要按两次快捷键才看得见窗口。
    /// 例外：设置窗口打开时焦点在对话框上（主窗口必然不是活动窗口），此时按键收起主窗口更合理；
    /// 置顶且可见时窗口本来就压在别人上面，也不必"先唤前"。
    /// </summary>
    public static bool ShouldRaiseOnHotkey(bool hidden, bool isActive, bool modalOpen, bool topmost)
        => hidden || (!isActive && !modalOpen && !topmost);
}
