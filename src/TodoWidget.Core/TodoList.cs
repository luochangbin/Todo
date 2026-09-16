namespace TodoWidget.Core;

public sealed class TodoList
{
    private static readonly TimeSpan WeekWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan MonthWindow = TimeSpan.FromDays(30);

    private readonly IClock _clock;
    private readonly List<TodoItem> _items;

    public TodoList(IClock clock)
    {
        _clock = clock;
        _items = new List<TodoItem>();
    }

    public TodoList(IEnumerable<TodoItem> items, IClock clock)
    {
        _clock = clock;
        _items = Canonicalize(items);
    }

    public IReadOnlyList<TodoItem> Items => _items.ToList();

    // 新事项插入普通区开头（最新在上），不会越过置顶项。
    public void Create(string text)
    {
        var id = Guid.NewGuid();
        var item = new TodoItem(id, text, false, 0, _clock.UtcNow);
        _items.Insert(FirstActiveIndex, item);
        NormalizeOrders();
    }

    public void UpdateText(Guid id, string text)
    {
        int index = FindIndex(id);
        var item = _items[index];
        _items[index] = item with { Text = text, LastActiveAt = _clock.UtcNow };
    }

    public void SetCompleted(Guid id, bool isCompleted)
    {
        int index = FindIndex(id);
        var item = _items[index];
        if (item.IsCompleted == isCompleted) return;

        _items.RemoveAt(index);
        var updated = item with { IsCompleted = isCompleted, LastActiveAt = _clock.UtcNow };
        if (isCompleted)
        {
            // 完成后与普通事项一致：进已完成组首位（置顶状态不影响已完成组排序）
            int incompleteCount = _items.Count(i => !i.IsCompleted);
            _items.Insert(incompleteCount, updated);
        }
        else
        {
            // 取消完成：回到所在子区的首位（置顶项回置顶区首位，普通项回普通区首位）
            _items.Insert(updated.IsPinned ? 0 : FirstActiveIndex, updated);
        }
        NormalizeOrders();
    }

    /// <summary>置顶：进入未完成组的置顶区末尾，多个置顶按置顶先后排列。已完成事项不可置顶。</summary>
    public void Pin(Guid id)
    {
        int index = FindIndex(id);
        var item = _items[index];
        if (item.IsCompleted || item.IsPinned) return;

        _items.RemoveAt(index);
        _items.Insert(PinnedCount, item with { IsPinned = true });
        NormalizeOrders();
    }

    /// <summary>取消置顶：回到普通区首位。</summary>
    public void Unpin(Guid id)
    {
        int index = FindIndex(id);
        var item = _items[index];
        if (!item.IsPinned) return;

        _items.RemoveAt(index);
        _items.Insert(FirstActiveIndex, item with { IsPinned = false });
        NormalizeOrders();
    }

    public void Move(Guid id, int targetIndex)
    {
        int index = FindIndex(id);
        var item = _items[index];
        int clamped = ClampMoveTarget(id, targetIndex);
        if (clamped == index) return;

        _items.RemoveAt(index);
        _items.Insert(clamped, item);
        NormalizeOrders();
    }

    // 拖动预览与 Move 共用同一套子区夹取规则，保证预览落点与最终落点一致
    public int ClampMoveTarget(Guid id, int targetIndex)
    {
        var item = _items[FindIndex(id)];
        int incompleteCount = _items.Count(i => !i.IsCompleted);

        if (item.IsCompleted)
        {
            // 已完成项只在已完成子区内重排
            return Math.Clamp(targetIndex, incompleteCount, _items.Count - 1);
        }
        if (item.IsPinned)
        {
            // 置顶项只在置顶子区内重排
            return Math.Clamp(targetIndex, 0, PinnedCount - 1);
        }
        // 普通项只在普通子区内重排
        return Math.Clamp(targetIndex, PinnedCount, incompleteCount - 1);
    }

    public void Delete(Guid id)
    {
        int index = FindIndex(id);
        _items.RemoveAt(index);
        NormalizeOrders();
    }

    // 未完成事项始终显示；只有已完成事项按显示范围过滤。过滤只作用于返回值，
    // 不修改全量集合、完成状态或顺序，因此超范围事项仍完整持久化。
    public IReadOnlyList<TodoItem> GetVisible(CompletedRange range)
    {
        if (range == CompletedRange.All) return Items;
        TimeSpan window = range == CompletedRange.Month ? MonthWindow : WeekWindow;
        DateTimeOffset cutoff = _clock.UtcNow - window;
        return _items.Where(i => !i.IsCompleted || !(i.LastActiveAt < cutoff)).ToList();
    }

    /// <summary>物理顺序即渲染顺序，这里只按子区重新编号。</summary>
    private int PinnedCount => _items.Count(i => i.IsPinned && !i.IsCompleted);

    /// <summary>未完成组里第一个非置顶项的下标，也就是普通区开头。</summary>
    private int FirstActiveIndex => PinnedCount;

    private int FindIndex(Guid id)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Id == id) return i;
        }
        throw new ArgumentException($"未知事项 Id: {id}", nameof(id));
    }

    private void NormalizeOrders()
    {
        int pinnedOrdinal = 0;
        int activeOrdinal = 0;
        int completedOrdinal = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            _items[i] = item.IsCompleted
                ? item with { Order = completedOrdinal++ }
                : item.IsPinned
                    ? item with { PinOrder = pinnedOrdinal++, Order = 0 }
                    : item with { Order = activeOrdinal++ };
        }
    }

    // 加载时按不变量重建物理顺序：置顶未完成(按置顶序) -> 普通未完成(按 Order) -> 已完成(按 Order)
    private static List<TodoItem> Canonicalize(IEnumerable<TodoItem> source)
    {
        var ordered = source
            .OrderBy(GroupKey)
            .ThenBy(SortOrderKey)
            .ThenBy(i => i.Id)
            .ToList();

        int pinnedOrdinal = 0;
        int activeOrdinal = 0;
        int completedOrdinal = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            var item = ordered[i];
            ordered[i] = item.IsCompleted
                ? item with { Order = completedOrdinal++ }
                : item.IsPinned
                    ? item with { PinOrder = pinnedOrdinal++, Order = 0 }
                    : item with { Order = activeOrdinal++ };
        }
        return ordered;
    }

    private static int GroupKey(TodoItem item) => item.IsCompleted ? 2 : item.IsPinned ? 0 : 1;

    private static int SortOrderKey(TodoItem item) =>
        !item.IsCompleted && item.IsPinned ? item.PinOrder : item.Order;
}
