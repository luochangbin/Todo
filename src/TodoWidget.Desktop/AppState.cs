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
        var result = await Repository.SaveAsync(new StateSnapshot(Todos.Items, Settings));
        return result.Success ? null : result.Error;
    }
}
