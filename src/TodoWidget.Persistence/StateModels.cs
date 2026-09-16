using TodoWidget.Core;

namespace TodoWidget.Persistence;

public sealed record StateSnapshot(IReadOnlyList<TodoItem> Items, AppSettings Settings);

public sealed record StateLoadResult(
    bool Success,
    bool FileMissing,
    bool UnsupportedVersion,
    IReadOnlyList<TodoItem> Items,
    AppSettings Settings,
    string? Error);

public sealed record StateSaveResult(bool Success, string? Error);
