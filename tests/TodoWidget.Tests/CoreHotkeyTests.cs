using TodoWidget.Core;

namespace TodoWidget.Tests;

public static class CoreHotkeyTests
{
    public static TestSuite Suite() => new("Core: HotkeyParser 解析/规范化/冲突", new (string, Action)[]
    {
        ("合法组合 Ctrl+Alt+Space 解析出两个修饰键与主键", () =>
        {
            Test.True(HotkeyParser.TryParse("Ctrl+Alt+Space", out var h, out var e), $"应解析成功: {e}");
            Test.True(h.Ctrl && h.Alt && !h.Shift && !h.Win, "应识别 Ctrl 与 Alt");
            Test.Eq(h.Key, HotkeyKey.Space);
            Test.Eq(h.ToDisplay(), "Ctrl+Alt+Space");
        }),
        ("单个修饰键合法（至少一个修饰键即可）", () =>
        {
            Test.True(HotkeyParser.TryParse("Ctrl+M", out var h, out var e), e);
            Test.True(h.Ctrl);
            Test.Eq(h.Key, HotkeyKey.M);
            Test.Eq(h.ToDisplay(), "Ctrl+M");
        }),
        ("输入可大小写混写并容忍空白", () =>
        {
            Test.True(HotkeyParser.TryParse(" ctrl + alt + m ", out var h, out var e), e);
            Test.True(h.Ctrl && h.Alt);
            Test.Eq(h.Key, HotkeyKey.M);
            Test.Eq(h.ToDisplay(), "Ctrl+Alt+M");
        }),
        ("不同书写顺序规范化后显示一致", () =>
        {
            Test.True(HotkeyParser.TryParse("Alt+M+Ctrl", out var a, out var _), "应解析 Alt+M+Ctrl");
            Test.True(HotkeyParser.TryParse("Ctrl+Alt+M", out var b, out var _), "应解析 Ctrl+Alt+M");
            Test.Eq(a, b, "顺序不同的同一组合应等价");
            Test.Eq(a.ToDisplay(), "Ctrl+Alt+M");
        }),
        ("缺失修饰键被拒绝", () =>
        {
            Test.True(!HotkeyParser.TryParse("M", out _, out var e));
            Test.True(!string.IsNullOrEmpty(e));
        }),
        ("空输入/纯空白被拒绝", () =>
        {
            Test.True(!HotkeyParser.TryParse("", out _, out var e1));
            Test.True(!string.IsNullOrEmpty(e1));
            Test.True(!HotkeyParser.TryParse("   ", out _, out var e2));
            Test.True(!string.IsNullOrEmpty(e2));
        }),
        ("只有修饰键没有主键被拒绝", () =>
        {
            Test.True(!HotkeyParser.TryParse("Ctrl+Alt+", out _, out var e));
            Test.True(!HotkeyParser.TryParse("Ctrl+Alt", out _, out _));
        }),
        ("重复修饰键被拒绝", () =>
        {
            Test.True(!HotkeyParser.TryParse("Ctrl+Ctrl+M", out _, out var e));
            Test.True(!string.IsNullOrEmpty(e));
        }),
        ("未知键名被拒绝", () =>
        {
            Test.True(!HotkeyParser.TryParse("Ctrl+Xtra", out _, out var e));
            Test.True(!string.IsNullOrEmpty(e));
            Test.True(!HotkeyParser.TryParse("Ctrl+Alt+???", out _, out _));
        }),
        ("字母/数字/F键/方向键均可作主键", () =>
        {
            Test.True(HotkeyParser.TryParse("Ctrl+Shift+5", out var d, out _), "数字键应合法");
            Test.Eq(d.Key, HotkeyKey.D5);
            Test.True(HotkeyParser.TryParse("Alt+F5", out var f, out _), "F 键应合法");
            Test.Eq(f.Key, HotkeyKey.F5);
            Test.True(HotkeyParser.TryParse("Ctrl+Down", out var arrow, out _), "方向键应合法");
            Test.Eq(arrow.Key, HotkeyKey.Down);
        }),
        ("全修饰键组合 Win+Ctrl+Shift+Alt+Space 合法", () =>
        {
            Test.True(HotkeyParser.TryParse("Win+Ctrl+Shift+Alt+Space", out var h, out var e), e);
            Test.True(h.Win && h.Ctrl && h.Shift && h.Alt);
        }),
        ("单一快捷键：留空视为关闭（合法），合法组合通过", () =>
        {
            Test.Eq(AppHotkeyRules.ValidateToggleHotkey(""), null);
            Test.Eq(AppHotkeyRules.ValidateToggleHotkey("   "), null);
            Test.Eq(AppHotkeyRules.ValidateToggleHotkey("Alt+Q"), null);
            Test.Eq(AppHotkeyRules.ValidateToggleHotkey("Ctrl+Alt+Space"), null);
        }),
        ("单一快捷键：无效格式返回格式错误", () =>
        {
            var err = AppHotkeyRules.ValidateToggleHotkey("NotAKey");
            Test.True(!string.IsNullOrEmpty(err));
            Test.True(!string.IsNullOrEmpty(AppHotkeyRules.ValidateToggleHotkey("Q")), "缺少修饰键应被拒绝");
        }),
        ("默认快捷键是 Alt+Q 且可解析", () =>
        {
            Test.Eq(AppSettings.Default.ToggleHotkey, "Alt+Q");
            Test.True(HotkeyParser.TryParse(AppSettings.Default.ToggleHotkey, out var h, out _));
            Test.True(h.Alt && !h.Ctrl && !h.Shift && !h.Win);
            Test.Eq(h.Key, HotkeyKey.Q);
        }),
    });
}
