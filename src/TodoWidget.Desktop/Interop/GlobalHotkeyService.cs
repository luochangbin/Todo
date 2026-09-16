using System.Windows;
using System.Windows.Interop;
using TodoWidget.Core;

namespace TodoWidget.Desktop.Interop;

/// <summary>
/// 单一全局快捷键：注册/替换/注销，并接收 WM_HOTKEY。
/// 弹出与最小化共用同一个键，由回调自行判断当前状态。
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private Action? _action;
    private ParsedHotkey? _assigned;
    private int _currentId;
    private int _nextId = 100;

    public GlobalHotkeyService(Window window)
    {
        _hwnd = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd)!;
        _source.AddHook(WndProc);
    }

    public void RegisterHandler(Action action) => _action = action;

    /// <summary>配置替换：先注册新组合，成功后才注销旧组合；失败则继续沿用旧组合。</summary>
    public bool TryApply(ParsedHotkey? combo, out string? error)
    {
        error = null;
        if (_assigned == combo) return true;

        int oldId = _currentId;
        if (combo is null)
        {
            if (oldId != 0) Unregister(oldId);
            _currentId = 0;
            _assigned = null;
            return true;
        }

        int candidateId = _nextId++;
        if (!NativeMethods.RegisterHotKey(
                _hwnd,
                candidateId,
                HotkeyKeyMapper.Modifiers(combo),
                HotkeyKeyMapper.VirtualKey(combo.Key)))
        {
            error = $"快捷键 {combo.ToDisplay()} 已被其他程序占用";
            return false;
        }

        _assigned = combo;
        _currentId = candidateId;
        if (oldId != 0) Unregister(oldId);
        return true;
    }

    private void Unregister(int id) => NativeMethods.UnregisterHotKey(_hwnd, id);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _currentId != 0 && wParam.ToInt32() == _currentId)
        {
            _action?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        if (_currentId != 0)
        {
            NativeMethods.UnregisterHotKey(_hwnd, _currentId);
            _currentId = 0;
        }
        _assigned = null;
    }
}
