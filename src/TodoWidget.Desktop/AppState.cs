using TodoWidget.Core;
using TodoWidget.Persistence;

namespace TodoWidget.Desktop;

public sealed class AppState
{
    public AppState(TodoList todos, AppSettings settings, JsonStateRepository repository, string? startupError)
    {
        Todos = todos;
        Settings = settings;
        Repository = repository;
        StartupError = startupError;
    }

    public TodoList Todos { get; }
    public AppSettings Settings { get; set; }
    public JsonStateRepository Repository { get; }
    public string? StartupError { get; set; }

    public async Task<string?> SaveAsync()
    {
        // 快照必须在调用线程（通常是 UI 线程）上取，之后再让出到线程池做落盘；
        // ConfigureAwait(false) 避免同步等待保存的调用方死锁。
        var snapshot = new StateSnapshot(Todos.Items, Settings);
        var result = await Repository.SaveAsync(snapshot).ConfigureAwait(false);
        return result.Success ? null : result.Error;
    }
}
