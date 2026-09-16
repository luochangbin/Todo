namespace TodoWidget.Core;

public sealed record AppSettings(
    string BackgroundColor,
    string TextColor,
    double Opacity,
    bool AlwaysOnTop,
    string ToggleHotkey,
    CompletedRange CompletedRange,
    WindowPlacement Window,
    bool AutoStartConfigured = false)
{
    /// <summary>不透明度下界：低于此值窗口几乎不可见，读取配置时会被夹取回该值。</summary>
    public const double MinOpacity = 0.35;

    public const double MaxOpacity = 1.0;

    /// <summary>单一全局快捷键的默认值：弹出/最小化二合一。</summary>
    public const string DefaultToggleHotkey = "Alt+Q";

    /// <summary>背景色的特殊值：面板不填充也不描边，只有文字与控件浮在桌面上。</summary>
    public const string NoBackgroundColor = "none";

    public static bool IsNoBackground(string? color) =>
        string.Equals(color?.Trim(), NoBackgroundColor, StringComparison.OrdinalIgnoreCase);

    public static double ClampOpacity(double value) =>
        double.IsNaN(value) ? MaxOpacity : Math.Clamp(value, MinOpacity, MaxOpacity);

    public static AppSettings Default { get; } = new(
        "#1B1C20",
        "#E9EAEE",
        MaxOpacity,
        false,
        DefaultToggleHotkey,
        CompletedRange.Week,
        WindowPlacement.Default);
}
