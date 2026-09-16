using TodoWidget.Core;

namespace TodoWidget.Tests;

public static class CoreDockTests
{
    private static readonly RectD Work = new(0, 0, 1280, 800);

    public static TestSuite Suite() => new("Core: DockCalculator 停靠几何", new (string, Action)[]
    {
        ("贴右边缘释放检测为 Right", () =>
        {
            var w = new RectD(950, 300, 330, 420); // Right == 1280
            Test.Eq(DockCalculator.DetectDockEdge(w, Work), DockEdge.Right);
        }),
        ("贴左边缘检测为 Left", () =>
        {
            var w = new RectD(0, 300, 330, 420);
            Test.Eq(DockCalculator.DetectDockEdge(w, Work), DockEdge.Left);
        }),
        ("贴顶边缘检测为 Top", () =>
        {
            var w = new RectD(500, 0, 330, 420);
            Test.Eq(DockCalculator.DetectDockEdge(w, Work), DockEdge.Top);
        }),
        ("贴底边缘检测为 Bottom", () =>
        {
            var w = new RectD(300, 380, 330, 420); // Bottom == 800
            Test.Eq(DockCalculator.DetectDockEdge(w, Work), DockEdge.Bottom);
        }),
        ("18px 内算接近边缘，19px 不算", () =>
        {
            Test.Eq(DockCalculator.DetectDockEdge(new RectD(945, 300, 330, 420), Work), DockEdge.Right);  // 距右 5
            Test.Eq(DockCalculator.DetectDockEdge(new RectD(932, 300, 330, 420), Work), DockEdge.Right);  // 距右恰 18
            Test.Eq(DockCalculator.DetectDockEdge(new RectD(931, 300, 330, 420), Work), null);            // 距右 19
        }),
        ("远离所有边缘不产生停靠", () =>
        {
            Test.Eq(DockCalculator.DetectDockEdge(new RectD(400, 200, 330, 420), Work), null);
        }),
        ("接近角点选择距离最小的边缘", () =>
        {
            var w = new RectD(1240, 300, 40, 40); // 距右 0，距底 460 -> Right
            Test.Eq(DockCalculator.DetectDockEdge(w, Work), DockEdge.Right);
            var w2 = new RectD(1240, 760, 40, 40); // Right 0, Bottom 0 平局 -> Right (确定性)
            Test.Eq(DockCalculator.DetectDockEdge(w2, Work), DockEdge.Right);
        }),
        ("右侧收拢：整体移出工作区右侧，尺寸与纵向位置保持", () =>
        {
            var w = new RectD(950, 300, 330, 420);
            var c = DockCalculator.CollapsedRect(w, DockEdge.Right, Work);
            Test.Near(c.Left, 1280);
            Test.Near(c.Width, 330);
            Test.Near(c.Top, 300);
            Test.Near(c.Height, 420);
        }),
        ("顶部收拢：整体移出工作区上方，尺寸与横向位置保持", () =>
        {
            var w = new RectD(500, 10, 330, 420);
            var c = DockCalculator.CollapsedRect(w, DockEdge.Top, Work);
            Test.Near(c.Bottom, 0);
            Test.Near(c.Height, 420);
            Test.Near(c.Left, 500);
            Test.Near(c.Width, 330);
        }),
        ("垂直边缘锚点取窗口中心在纵向的相对位置", () =>
        {
            var w = new RectD(950, 300, 330, 420); // centerY=510, work 800
            var a = DockCalculator.ComputeAnchor(w, DockEdge.Right, Work);
            Test.Near(a, 510.0 / 800.0);
            var w2 = new RectD(950, 10, 330, 420); // centerY=220
            Test.Near(DockCalculator.ComputeAnchor(w2, DockEdge.Left, Work), 220.0 / 800.0);
        }),
        ("水平边缘锚点取窗口中心在横向的相对位置", () =>
        {
            var w = new RectD(500, 380, 330, 420); // centerX=665, work 1280
            var a = DockCalculator.ComputeAnchor(w, DockEdge.Top, Work);
            Test.Near(a, 665.0 / 1280.0);
        }),
        ("位置校正：与任一工作区相交的保存坐标原样保留", () =>
        {
            var p = new WindowPlacement(100, 100, 330, 420, DockEdge.None, 0);
            var corrected = DockCalculator.CorrectPlacement(p, new[] { Work }, Work);
            Test.Eq(corrected, p);
            var docked = p with { DockEdge = DockEdge.Right, Anchor = 0.5 };
            Test.Eq(DockCalculator.CorrectPlacement(docked, new[] { Work }, Work), docked);
        }),
        ("位置校正：完全越界（显示器拔出）回退主屏可见中心并清除停靠", () =>
        {
            var offscreen = new WindowPlacement(-3000, -4000, 330, 420, DockEdge.None, 0);
            var corrected = DockCalculator.CorrectPlacement(offscreen, new[] { Work }, Work);
            Test.Eq(corrected.DockEdge, DockEdge.None);
            Test.Near(corrected.Left, (1280 - 330) / 2.0);
            Test.Near(corrected.Top, (800 - 420) / 2.0);
            var offscreenDocked = new WindowPlacement(9000, 9000, 330, 420, DockEdge.Right, 0.2);
            var corrected2 = DockCalculator.CorrectPlacement(offscreenDocked, new[] { Work }, Work);
            Test.Eq(corrected2.DockEdge, DockEdge.None);
        }),
        ("收拢矩形完全移出工作区（0 像素可见）", () =>
        {
            var w = new RectD(1240, 200, 40, 40);
            foreach (var edge in new[] { DockEdge.Left, DockEdge.Right, DockEdge.Top, DockEdge.Bottom })
            {
                var c = DockCalculator.CollapsedRect(w, edge, Work);
                Test.Assert(!c.Intersects(Work), $"边缘 {edge} 收拢矩形 {c} 应与工作区无任何交集");
            }
        }),
        ("完整位置夹取：一半在屏幕外时挪回工作区内", () =>
        {
            var half = new RectD(-180, 300, 330, 420);
            var c = DockCalculator.ClampIntoWorkArea(half, Work);
            Test.Near(c.Left, 0);
            Test.Near(c.Top, 300);
            Test.Near(c.Width, 330);
            Test.Near(c.Height, 420);
        }),
        ("完整位置夹取：右下越界时回到可见范围", () =>
        {
            var c = DockCalculator.ClampIntoWorkArea(new RectD(1200, 700, 330, 420), Work);
            Test.Near(c.Left, 1280 - 330);
            Test.Near(c.Top, 800 - 420);
        }),
        ("完整位置夹取：比工作区还大时收缩到工作区尺寸", () =>
        {
            var c = DockCalculator.ClampIntoWorkArea(new RectD(100, 100, 2000, 2000), Work);
            Test.Near(c.Width, 1280);
            Test.Near(c.Height, 800);
            Test.Near(c.Left, 0);
            Test.Near(c.Top, 0);
        }),
        ("恢复触发：光标贴住停靠边缘且落在原停靠位置范围内", () =>
        {
            var docked = new RectD(950, 300, 330, 420); // 右侧停靠，纵向 300..720
            Test.True(DockCalculator.IsRestoreTrigger(1279, 500, docked, DockEdge.Right, Work));
            Test.True(DockCalculator.IsRestoreTrigger(1280, 300, docked, DockEdge.Right, Work), "范围边界也算");
            Test.Assert(!DockCalculator.IsRestoreTrigger(1279, 100, docked, DockEdge.Right, Work),
                "纵向远离原停靠位置不应触发");
            Test.Assert(!DockCalculator.IsRestoreTrigger(600, 500, docked, DockEdge.Right, Work),
                "离开屏幕边缘不应触发");
        }),
        ("恢复触发：左/上/下边缘与未停靠状态", () =>
        {
            Test.True(DockCalculator.IsRestoreTrigger(0, 500, new RectD(20, 300, 330, 420), DockEdge.Left, Work));
            Test.Assert(!DockCalculator.IsRestoreTrigger(30, 500, new RectD(20, 300, 330, 420), DockEdge.Left, Work),
                "离左边缘 30px 不应触发");
            Test.True(DockCalculator.IsRestoreTrigger(600, 0, new RectD(500, 20, 330, 420), DockEdge.Top, Work));
            Test.True(DockCalculator.IsRestoreTrigger(600, 800, new RectD(500, 360, 330, 420), DockEdge.Bottom, Work));
            Test.Assert(!DockCalculator.IsRestoreTrigger(0, 0, new RectD(0, 0, 330, 420), DockEdge.None, Work),
                "未停靠时永不触发恢复");
        }),
    });
}
