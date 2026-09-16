using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace TodoWidget.Desktop;

/// <summary>
/// 托盘图标（通知区域）。主窗口本身不占任务栏，托盘提供唯一的可见入口：
/// 双击切换显示/隐藏，右键菜单可显示/隐藏或退出。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private bool _disposed;

    public TrayIcon(Action onToggle, Action onExit)
    {
        var menu = new WinForms.ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add("显示 / 隐藏", null, (_, _) => onToggle());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => onExit());

        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "Todo",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => onToggle();
    }

    /// <summary>
    /// 取托盘图标：优先用内嵌的 todo.ico，并按系统小图标尺寸（16/20/24，随 DPI）
    /// 挑选对应档位——否则会把 32px 硬缩到托盘尺寸，小尺寸下发虚。
    /// </summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            var assembly = typeof(TrayIcon).Assembly;
            string? name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("todo.ico", StringComparison.OrdinalIgnoreCase));
            if (name is not null)
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                return new Icon(stream, WinForms.SystemInformation.SmallIconSize);
            }
        }
        catch
        {
            // 内嵌资源不可用时退回 exe 关联图标
        }

        string? path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            var extracted = Icon.ExtractAssociatedIcon(path);
            if (extracted is not null) return extracted;
        }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
