using System.IO;
using System.Threading;
using System.Windows;
using TodoWidget.Core;
using TodoWidget.Persistence;

namespace TodoWidget.Desktop;

public partial class App : Application
{
    private const string MutexName = @"Local\TodoWidget.Desktop.SingleInstance";
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash("DispatcherUnhandledException", args.Exception);
            MessageBox.Show($"TodoWidget 发生未处理错误：{args.Exception.Message}", "TodoWidget");
            args.Handled = true;
        };

        bool createdNew;
        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out createdNew);
        if (!createdNew)
        {
            MessageBox.Show("TodoWidget 已在运行，请切换到已有窗口。", "TodoWidget");
            Shutdown();
            return;
        }

        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            string dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TodoWidget");

            var repository = new JsonStateRepository(dataDir);
            var load = await repository.LoadAsync();
            var todos = new TodoList(load.Items, SystemClock.Instance);
            var state = new AppState(todos, load.Settings, repository, load.Error);

            var mainWindow = new MainWindow(state);
            MainWindow = mainWindow;
            mainWindow.Initialize();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            LogCrash("StartAsync", ex);
            MessageBox.Show($"TodoWidget 启动失败：{ex.Message}", "TodoWidget");
            Shutdown();
        }
    }

    private static void LogCrash(string stage, Exception exception)
    {
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), "TodoWidget-crash.log");
            File.AppendAllText(logPath, $"[{DateTimeOffset.Now:O}] {stage}: {exception}{Environment.NewLine}");
        }
        catch { /* logging must never crash the app */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }
}
