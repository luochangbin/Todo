using Microsoft.Win32;

namespace TodoWidget.Desktop;

/// <summary>
/// 随系统启动：写当前用户的 Run 项，不需要管理员权限，也不影响其他用户。
/// 首次运行后开启；从托盘菜单退出时取消。
/// </summary>
public static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TodoWidget";

    /// <summary>当前可执行文件的启动命令行（路径带引号以兼容空格）。</summary>
    public static string? CommandLine()
    {
        string? exe = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(exe) ? null : $"\"{exe}\"";
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static bool Enable()
    {
        string? command = CommandLine();
        if (command is null) return false;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key?.SetValue(ValueName, command);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is null) return true;
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
