using System.Globalization;
using System.Windows.Media;
using TodoWidget.Core;

namespace TodoWidget.Desktop;

public static class ColorUtil
{
    public static bool TryParse(string? text, out Color color)
    {
        color = Colors.Black;
        if (text is null) return false;
        var t = text.Trim().TrimStart('#');
        if (t.Length is not (6 or 8)) return false;
        if (!byte.TryParse(t.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a)) return false;
        if (!byte.TryParse(t.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return false;
        if (!byte.TryParse(t.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var c)) return false;
        byte alpha = 255;
        if (t.Length == 8)
        {
            if (!byte.TryParse(t.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out alpha)) return false;
        }
        // #RRGGBB or #AARRGGBB
        color = t.Length == 8
            ? Color.FromArgb(alpha, a, b, c)
            : Color.FromRgb(a, b, c);
        return true;
    }

    public static double Luminance(Color color) =>
        (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;

    public static Color Mix(Color color, Color target, double amount)
    {
        double t = Math.Clamp(amount, 0.0, 1.0);
        byte Blend(byte from, byte to) => (byte)Math.Round(from + (to - from) * t);
        return Color.FromArgb(color.A, Blend(color.R, target.R), Blend(color.G, target.G), Blend(color.B, target.B));
    }

    /// <summary>只替换 alpha，用于把面板底色按不透明度设置做半透明处理，文字色不受影响。</summary>
    public static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    public static byte AlphaFromOpacity(double opacity) =>
        (byte)Math.Round(AppSettings.ClampOpacity(opacity) * 255.0);

    /// <summary>
    /// 面板上的文字/图标的 alpha：平滑跟随面板不透明度，但保留可读下限
    /// （面板 100% → 文字 100%；面板 35% → 文字约 67%）。
    /// </summary>
    public static byte TextAlphaFromOpacity(double opacity) =>
        (byte)Math.Round((0.5 + 0.5 * AppSettings.ClampOpacity(opacity)) * 255.0);

    // 由背景色派生一个相对色。纯黑/纯白等极端底色下自动反向，保证与底色仍有可见区分。
    public static Color Derive(Color baseColor, double amount, bool towardWhite)
    {
        var primary = Mix(baseColor, towardWhite ? Colors.White : Colors.Black, amount);
        if (Math.Abs(Luminance(primary) - Luminance(baseColor)) >= 0.02) return primary;
        return Mix(baseColor, towardWhite ? Colors.Black : Colors.White, amount);
    }
}
