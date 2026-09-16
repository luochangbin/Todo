namespace TodoWidget.Core;

public sealed record TodoItem(
    Guid Id,
    string Text,
    bool IsCompleted,
    int Order,
    DateTimeOffset LastActiveAt,
    bool IsPinned = false,
    int PinOrder = 0);
