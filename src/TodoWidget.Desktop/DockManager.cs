using System.Windows;
using System.Windows.Threading;
using TodoWidget.Core;
using TodoWidget.Desktop.Interop;

namespace TodoWidget.Desktop;

/// <summary>
/// 管理主窗口的三种几何状态与停靠信息的持久化：
///   Normal        普通浮动
///   Collapsed     已停靠，窗口整体移出屏幕（0 像素可见）
///   ShownFromDock 因光标触发边缘而滑出显示，鼠标离开后会再次收回
/// 完全移出屏幕后窗口收不到鼠标事件，因此靠低频光标监视触发滑出。
/// 坐标统一使用 WPF 设备无关单位（DIP）。
/// </summary>
public sealed class DockManager : IDisposable
{
    private static readonly TimeSpan RestorePollInterval = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan CollapseDelay = TimeSpan.FromMilliseconds(420);

    private readonly Window _window;
    private readonly Action<WindowPlacement> _placementChanged;
    private readonly DispatcherTimer _restoreWatcher;
    private readonly DispatcherTimer _collapseWatcher;
    private RectD _fullRect = new(0, 0, 0, 0);
    private RectD _workArea = new(0, 0, 0, 0);
    private DockEdge _edge = DockEdge.None;
    private double _anchor;
    private bool _collapsed;

    public DockManager(Window window, Action<WindowPlacement> placementChanged)
    {
        _window = window;
        _placementChanged = placementChanged;

        _restoreWatcher = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher)
        {
            Interval = RestorePollInterval,
        };
        _restoreWatcher.Tick += (_, _) => PollRestoreTrigger();

        _collapseWatcher = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher)
        {
            Interval = CollapseDelay,
        };
        _collapseWatcher.Tick += (_, _) => CollapseAfterLeave();
    }

    /// <summary>窗口当前是否处于「整体移出屏幕」状态。</summary>
    public bool IsCollapsed => _collapsed;

    public DockEdge Edge => _edge;

    public RectD FullRect => _fullRect;

    private double DpiScale => DpiHelper.GetWindowDpiScale(_window);

    public RectD WindowBounds => new(_window.Left, _window.Top, _window.Width, _window.Height);

    private RectD WorkAreaUnder(RectD rect)
    {
        var info = NativeMethods.MonitorInfoUnder(rect, DpiScale);
        if (info is null) return new RectD(0, 0, 1280, 800);
        double s = DpiScale;
        return new RectD(
            info.Work.Left / s, info.Work.Top / s,
            (info.Work.Right - info.Work.Left) / s,
            (info.Work.Bottom - info.Work.Top) / s);
    }

    /// <summary>在窗口 Show 前恢复/校正持久化放置。</summary>
    public void Initialize(WindowPlacement placement)
    {
        _fullRect = new RectD(placement.Left, placement.Top, placement.Width, placement.Height);
        SetBounds(_fullRect);

        if (placement.DockEdge != DockEdge.None)
        {
            _edge = placement.DockEdge;
            _anchor = placement.Anchor;
            _workArea = WorkAreaUnder(_fullRect);
            ApplyCollapsed();
        }
    }

    /// <summary>标题拖动结束：贴边则停靠，否则清除停靠并保存普通位置。</summary>
    public void OnTitleDragEnded()
    {
        _collapseWatcher.Stop();

        if (_collapsed)
        {
            // 隐藏态理论上拖不到，保底先恢复
            RestoreFromHidden();
            return;
        }

        var rect = WindowBounds;
        var work = WorkAreaUnder(rect);
        var edge = DockCalculator.DetectDockEdge(rect, work);
        if (edge is null)
        {
            _edge = DockEdge.None;
            _anchor = 0;
            _fullRect = rect;
            Persist(rect, DockEdge.None, 0);
            return;
        }

        // 保存「完整可见」的位置（夹取回工作区），避免恢复时只露出一半
        var full = DockCalculator.ClampIntoWorkArea(rect, work);
        _workArea = work;
        _fullRect = full;
        _edge = edge.Value;
        _anchor = DockCalculator.ComputeAnchor(full, edge.Value, work);
        ApplyCollapsed();
        Persist(full, _edge, _anchor);
    }

    /// <summary>窗口被拖动改大小后更新完整位置（由 MainWindow 在缩放结束时调用）。</summary>
    public void UpdateSizeFromWindow()
    {
        if (_collapsed) return;
        var rect = WindowBounds;
        var work = WorkAreaUnder(rect);
        var full = DockCalculator.ClampIntoWorkArea(rect, work);
        _fullRect = full;
        if (_edge != DockEdge.None) _workArea = work;
        Persist(full, _edge, _anchor);
    }

    /// <summary>鼠标离开窗口后延迟收回（仅在「因边缘触发而显示」时生效）。</summary>
    public void ScheduleCollapse()
    {
        if (_collapsed || _edge == DockEdge.None) return;
        _collapseWatcher.Stop();
        _collapseWatcher.Start();
    }

    /// <summary>把窗口从隐藏态滑出显示；停靠状态保留，鼠标离开后会再次收回。</summary>
    public void RestoreFromHidden()
    {
        if (!_collapsed) return;
        _collapseWatcher.Stop();
        _restoreWatcher.Stop();
        _collapsed = false;
        SetBounds(_fullRect);
        Persist(_fullRect, _edge, _anchor);
    }

    public WindowPlacement CurrentPlacement =>
        new(_fullRect.Left, _fullRect.Top, _fullRect.Width, _fullRect.Height, _edge, _anchor);

    private void ApplyCollapsed()
    {
        _collapseWatcher.Stop();
        _collapsed = true;
        SetBounds(DockCalculator.CollapsedRect(_fullRect, _edge, _workArea));
        if (!_restoreWatcher.IsEnabled) _restoreWatcher.Start();
    }

    private void CollapseAfterLeave()
    {
        _collapseWatcher.Stop();
        if (_collapsed || _edge == DockEdge.None) return;
        if (_window.IsMouseOver) return;   // 鼠标又回来了
        ApplyCollapsed();
        Persist(_fullRect, _edge, _anchor);
    }

    // 完全移出屏幕后窗口收不到鼠标事件，只能靠光标位置判定是否该滑出
    private void PollRestoreTrigger()
    {
        if (!_collapsed)
        {
            _restoreWatcher.Stop();
            return;
        }
        if (!NativeMethods.GetCursorPos(out var point)) return;

        double scale = DpiScale;
        if (scale <= 0) scale = 1.0;
        if (DockCalculator.IsRestoreTrigger(point.X / scale, point.Y / scale, _fullRect, _edge, _workArea))
        {
            RestoreFromHidden();
        }
    }

    private void Persist(RectD rect, DockEdge edge, double anchor) =>
        _placementChanged(new WindowPlacement(rect.Left, rect.Top, rect.Width, rect.Height, edge, anchor));

    private void SetBounds(RectD rect)
    {
        _window.Left = rect.Left;
        _window.Top = rect.Top;
        _window.Width = rect.Width;
        _window.Height = rect.Height;
    }

    public void Dispose()
    {
        _restoreWatcher.Stop();
        _collapseWatcher.Stop();
    }
}
