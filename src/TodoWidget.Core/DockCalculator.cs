namespace TodoWidget.Core;

public static class DockCalculator
{
    public const double EdgeThreshold = 18;

    /// <summary>光标进入屏幕边缘多少像素内算触发恢复。</summary>
    public const double RestoreTriggerThickness = 4;

    public static DockEdge? DetectDockEdge(RectD window, RectD workArea)
    {
        var candidates = new (double Distance, DockEdge Edge)[]
        {
            (workArea.Right - window.Right, DockEdge.Right),
            (workArea.Bottom - window.Bottom, DockEdge.Bottom),
            (window.Left - workArea.Left, DockEdge.Left),
            (window.Top - workArea.Top, DockEdge.Top),
        };

        DockEdge? best = null;
        double bestDistance = double.MaxValue;
        int bestPriority = int.MaxValue;
        for (int i = 0; i < candidates.Length; i++)
        {
            var (distance, edge) = candidates[i];
            if (distance > EdgeThreshold) continue;
            // distance < 0 表示窗口已越过该边缘，同样视为贴边
            if (distance < bestDistance
                || (Math.Abs(distance - bestDistance) < 1e-9 && i < bestPriority))
            {
                best = edge;
                bestDistance = distance;
                bestPriority = i;
            }
        }
        return best;
    }

    /// <summary>
    /// 收拢位置：把窗口整体移出对应屏幕边缘（保持原尺寸），做到 0 像素可见。
    /// 触发恢复靠屏幕边缘的光标判定，而不是窗口自身的命中区域。
    /// </summary>
    public static RectD CollapsedRect(RectD window, DockEdge edge, RectD workArea)
    {
        return edge switch
        {
            DockEdge.Left => new RectD(workArea.Left - window.Width, window.Top, window.Width, window.Height),
            DockEdge.Right => new RectD(workArea.Right, window.Top, window.Width, window.Height),
            DockEdge.Top => new RectD(window.Left, workArea.Top - window.Height, window.Width, window.Height),
            DockEdge.Bottom => new RectD(window.Left, workArea.Bottom, window.Width, window.Height),
            _ => window,
        };
    }

    /// <summary>把矩形整体挪进工作区（必要时缩小），保证恢复后完整可见。</summary>
    public static RectD ClampIntoWorkArea(RectD rect, RectD workArea)
    {
        double width = Math.Min(rect.Width, workArea.Width);
        double height = Math.Min(rect.Height, workArea.Height);
        double left = Math.Clamp(rect.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
        double top = Math.Clamp(rect.Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));
        return new RectD(left, top, width, height);
    }

    /// <summary>
    /// 光标是否落在「停靠边缘 + 原停靠位置范围」的触发区内。
    /// </summary>
    public static bool IsRestoreTrigger(
        double cursorX,
        double cursorY,
        RectD dockedRect,
        DockEdge edge,
        RectD workArea,
        double thickness = RestoreTriggerThickness)
    {
        bool withinVerticalSpan = cursorY >= dockedRect.Top - thickness && cursorY <= dockedRect.Bottom + thickness;
        bool withinHorizontalSpan = cursorX >= dockedRect.Left - thickness && cursorX <= dockedRect.Right + thickness;
        return edge switch
        {
            DockEdge.Left => cursorX <= workArea.Left + thickness && withinVerticalSpan,
            DockEdge.Right => cursorX >= workArea.Right - thickness && withinVerticalSpan,
            DockEdge.Top => cursorY <= workArea.Top + thickness && withinHorizontalSpan,
            DockEdge.Bottom => cursorY >= workArea.Bottom - thickness && withinHorizontalSpan,
            _ => false,
        };
    }

    public static double ComputeAnchor(RectD window, DockEdge edge, RectD workArea)
    {
        double fraction = edge switch
        {
            DockEdge.Left or DockEdge.Right =>
                (window.Top + window.Height / 2 - workArea.Top) / workArea.Height,
            DockEdge.Top or DockEdge.Bottom =>
                (window.Left + window.Width / 2 - workArea.Left) / workArea.Width,
            _ => 0,
        };
        return Math.Clamp(fraction, 0, 1);
    }

    public static WindowPlacement CorrectPlacement(
        WindowPlacement placement, IReadOnlyList<RectD> workAreas, RectD primaryWorkArea)
    {
        var rect = new RectD(placement.Left, placement.Top, placement.Width, placement.Height);
        if (workAreas.Any(rect.Intersects))
        {
            return placement;
        }
        double width = rect.Width <= 0 ? WindowPlacement.Default.Width : rect.Width;
        double height = rect.Height <= 0 ? WindowPlacement.Default.Height : rect.Height;
        double left = primaryWorkArea.Left + (primaryWorkArea.Width - width) / 2;
        double top = primaryWorkArea.Top + (primaryWorkArea.Height - height) / 2;
        return new WindowPlacement(left, top, width, height, DockEdge.None, 0);
    }
}
