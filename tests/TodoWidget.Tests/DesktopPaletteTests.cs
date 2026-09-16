using System.Windows.Media;
using TodoWidget.Desktop;

namespace TodoWidget.Tests;

public static class DesktopPaletteTests
{
    public static TestSuite Suite() => new("UI 外观 token", new (string, Action)[]
    {
        ("Mix 两端分别返回原色与目标色", MixEndpoints),
        ("Derive 提亮变亮、压暗变暗", DeriveDirection),
        ("Derive 纯黑底仍与底色保持可见区分", DeriveOnBlackStaysDistinct),
        ("Derive 纯白底仍与底色保持可见区分", DeriveOnWhiteStaysDistinct),
    });

    private static void MixEndpoints()
    {
        var baseColor = Color.FromRgb(0x1B, 0x1C, 0x20);
        Test.Eq(ColorUtil.Mix(baseColor, Colors.White, 0.0), baseColor);
        Test.Eq(ColorUtil.Mix(baseColor, Colors.White, 1.0), Colors.White);
    }

    private static void DeriveDirection()
    {
        var baseColor = Color.FromRgb(0x1B, 0x1C, 0x20);
        Test.True(
            ColorUtil.Luminance(ColorUtil.Derive(baseColor, 0.25, towardWhite: true)) > ColorUtil.Luminance(baseColor),
            "向白派生应更亮");
        Test.True(
            ColorUtil.Luminance(ColorUtil.Derive(baseColor, 0.25, towardWhite: false)) < ColorUtil.Luminance(baseColor),
            "向黑派生应更暗");
    }

    private static void DeriveOnBlackStaysDistinct()
    {
        var derived = ColorUtil.Derive(Colors.Black, 0.25, towardWhite: false);
        Test.True(
            Math.Abs(ColorUtil.Luminance(derived) - ColorUtil.Luminance(Colors.Black)) >= 0.02,
            "纯黑底压暗必须反向提亮，否则输入框与底色无法区分");
    }

    private static void DeriveOnWhiteStaysDistinct()
    {
        var derived = ColorUtil.Derive(Colors.White, 0.25, towardWhite: true);
        Test.True(
            Math.Abs(ColorUtil.Luminance(derived) - ColorUtil.Luminance(Colors.White)) >= 0.02,
            "纯白底提亮必须反向压暗，否则分隔线与底色无法区分");
    }
}
