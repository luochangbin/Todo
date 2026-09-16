using TodoWidget.Core;

namespace TodoWidget.Tests;

public static class CoreListTests
{
    private static readonly DateTimeOffset Base = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static TestSuite Suite() => new("Core: TodoList 分组/排序/状态/历史", new (string, Action)[]
    {
        ("Create 插入到未完成组开头并记录 LastActiveAt=时钟", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            list.Create("B");
            var items = list.Items;
            Test.Eq(items.Count, 2);
            Test.Assert(items[0].IsCompleted == false && items[1].IsCompleted == false, "新建项都应为未完成");
            Test.Eq(items[0].Text, "B", "最新创建排在最前");
            Test.Eq(items[1].Text, "A");
            Test.Eq(items[0].Order, 0);
            Test.Eq(items[1].Order, 1);
            Test.Eq(items[0].LastActiveAt, Base);
            Test.Eq(items[1].LastActiveAt, Base);
        }),
        ("未完成组恒在已完成组之前（默认分组顺序不变量）", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            list.Create("B");
            list.Create("C");
            list.SetCompleted(IdOf(list, "B"), true);
            var items = list.Items;
            bool seenCompleted = false;
            foreach (var item in items)
            {
                if (item.IsCompleted) seenCompleted = true;
                else Test.Assert(!seenCompleted, "未完成项不能出现在已完成项之后");
            }
            Test.Eq(string.Join(",", items.Select(i => i.Text)), "C,A,B");
        }),
        ("勾选完成：移到已完成组首位，其余已完成保持相对顺序", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            list.Create("B");
            list.Create("C");
            list.Create("D");
            list.SetCompleted(IdOf(list, "A"), true);   // completed: [A]
            list.SetCompleted(IdOf(list, "C"), true);   // completed: [C,A]
            list.SetCompleted(IdOf(list, "B"), true);   // completed: [B,C,A]
            var items = list.Items;
            Test.Eq(string.Join(",", items.Select(i => i.Text)), "D,B,C,A");
            var completed = items.Where(i => i.IsCompleted).ToList();
            Test.Eq(string.Join(",", completed.Select(i => i.Text)), "B,C,A");
            Test.Eq(completed[0].Order, 0);
            Test.Eq(completed[1].Order, 1);
            Test.Eq(completed[2].Order, 2);
        }),
        ("取消完成：移到未完成组首位，其余组相对顺序保持", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            list.Create("B");
            list.Create("C");
            list.SetCompleted(IdOf(list, "A"), true);   // completed: [A]
            list.SetCompleted(IdOf(list, "C"), true);   // completed: [C,A], state render: [B,C,A]
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "B,C,A");
            list.SetCompleted(IdOf(list, "C"), false);  // 取消完成 -> 未完成首位
            var items = list.Items;
            Test.Eq(string.Join(",", items.Select(i => i.Text)), "C,B,A");
            var active = items.Where(i => !i.IsCompleted).ToList();
            Test.Eq(string.Join(",", active.Select(i => i.Text)), "C,B");
            Test.Eq(active[0].Order, 0);
            Test.Eq(active[1].Order, 1);
            var done = items.Where(i => i.IsCompleted).ToList();
            Test.Eq(string.Join(",", done.Select(i => i.Text)), "A");
        }),
        ("状态迁移更新 LastActiveAt", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            var created = Item(list, "A").LastActiveAt;
            clock.UtcNow = Base.AddMinutes(5);
            list.SetCompleted(IdOf(list, "A"), true);
            Test.Eq(Item(list, "A").LastActiveAt, Base.AddMinutes(5));
            clock.UtcNow = Base.AddMinutes(9);
            list.SetCompleted(IdOf(list, "A"), false);
            Test.Eq(Item(list, "A").LastActiveAt, Base.AddMinutes(9));
        }),
        ("相同状态迁移是无操作（不改时间/顺序）", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            list.Create("B");
            var before = list.Items.ToList();
            clock.UtcNow = Base.AddMinutes(30);
            list.SetCompleted(IdOf(list, "A"), false); // already incomplete
            Test.Eq(Item(list, "A").LastActiveAt, Base);
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "B,A");
            _ = before;
        }),
        ("同组拖动只重排源组并规范 Order", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            list.Create("B");
            list.Create("C");
            list.Create("X");
            list.Create("Y");
            list.SetCompleted(IdOf(list, "X"), true);
            list.SetCompleted(IdOf(list, "Y"), true);
            // 未完成组当前为 C,B,A；把 C 拖到未完成组最后（最终渲染索引 2）
            list.Move(IdOf(list, "C"), 2);
            var items = list.Items;
            Test.Eq(string.Join(",", items.Select(i => i.Text)), "B,A,C,Y,X");
            var active = items.Where(i => !i.IsCompleted).ToList();
            Test.Eq(string.Join(",", active.Select(i => i.Text)), "B,A,C");
            Test.Eq(active.Select(i => i.Order).ToArray(), new[] { 0, 1, 2 });
            var done = items.Where(i => i.IsCompleted).ToList();
            Test.Eq(string.Join(",", done.Select(i => i.Text)), "Y,X");
            Test.Eq(done.Select(i => i.Order).ToArray(), new[] { 0, 1 });
        }),
        ("已完成组内拖动不改变未完成组顺序", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            list.Create("B");
            list.Create("C");
            list.Create("D");
            list.Create("E");
            list.SetCompleted(IdOf(list, "C"), true);
            list.SetCompleted(IdOf(list, "D"), true);
            list.SetCompleted(IdOf(list, "E"), true);
            // 新建插入未完成组开头，故未完成组为 B,A；勾选完成是移到已完成组首位，故已完成组新到旧为 E,D,C
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "B,A,E,D,C");
            // D 拖到已完成组末尾 -> 最终渲染索引 4
            list.Move(IdOf(list, "D"), 4);
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "B,A,E,C,D");
            Test.Eq(string.Join(",", list.Items.Where(i => !i.IsCompleted).Select(i => i.Text)), "B,A");
        }),
        ("跨组拖动约束：已完成项拖入未完成区域限制到已完成组首位", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            list.Create("B");
            list.Create("C");
            list.Create("D");
            list.SetCompleted(IdOf(list, "C"), true);
            list.SetCompleted(IdOf(list, "D"), true);
            // 把 D（已完成）拖到未完成区域顶部（索引 0）
            list.Move(IdOf(list, "D"), 0);
            var items = list.Items;
            Test.Eq(string.Join(",", items.Select(i => i.Text)), "B,A,D,C");
            Test.Assert(!items.Take(2).Any(i => i.IsCompleted), "未完成项不得被已完成项挤到后面");
            Test.Eq(items[2].Text, "D");
            Test.Eq(items[2].IsCompleted, true);
        }),
        ("跨组拖动约束：未完成项拖入已完成区域限制到未完成组末尾", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            list.Create("B");
            list.Create("C");
            list.Create("D");
            list.SetCompleted(IdOf(list, "D"), true);
            // 把 A（未完成）拖到列表末尾（索引 3，已完成区域）
            list.Move(IdOf(list, "A"), 3);
            var items = list.Items;
            Test.Eq(string.Join(",", items.Select(i => i.Text)), "C,B,A,D");
            Test.Eq(items[3].Text, "D");
            Test.Eq(items[3].IsCompleted, true);
            Test.Assert(items.Take(3).All(i => !i.IsCompleted), "未完成项必须全部位于已完成项之前");
        }),
        ("越界 targetIndex 钳制到组合法边界", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            list.Create("B");
            list.Create("C");
            list.SetCompleted(IdOf(list, "C"), true);
            list.Move(IdOf(list, "A"), 99);
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "B,A,C");
            list.Move(IdOf(list, "C"), -5);
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "B,A,C");
        }),
        ("ClampMoveTarget 与 Move 落点完全一致", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            list.Create("B");
            list.Create("C");
            list.Create("D");
            list.Create("E");
            list.SetCompleted(IdOf(list, "A"), true);
            list.SetCompleted(IdOf(list, "B"), true);
            list.Pin(IdOf(list, "E"));

            int count = list.Items.Count;
            foreach (var item in list.Items.ToList())
            {
                for (int target = -3; target <= count + 3; target++)
                {
                    // 拖动预览用的就是 ClampMoveTarget，落点必须与它一致，否则松手会跳位
                    var probe = new TodoList(list.Items, clock);
                    int expected = probe.ClampMoveTarget(item.Id, target);
                    probe.Move(item.Id, target);

                    int actual = -1;
                    for (int i = 0; i < probe.Items.Count; i++)
                    {
                        if (probe.Items[i].Id == item.Id) { actual = i; break; }
                    }
                    Test.Eq(actual, expected, $"{item.Text} 拖到 {target}");
                }
            }
        }),
        ("Delete 移除事项并规范化同组 Order", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            list.Create("B");
            list.Create("C");
            list.SetCompleted(IdOf(list, "A"), true);
            list.SetCompleted(IdOf(list, "B"), true);
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "C,B,A");

            list.Delete(IdOf(list, "B"));
            Test.Eq(list.Items.Count, 2);
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "C,A");
            Test.Eq(string.Join(",", list.Items.Select(i => i.Order)), "0,0");
        }),
        ("Delete 不改变另一组的 Order", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("U1");
            list.Create("U2");
            list.Create("U3");
            list.SetCompleted(IdOf(list, "U3"), true);
            list.SetCompleted(IdOf(list, "U2"), true);

            var before = list.Items.Where(i => i.IsCompleted).Select(i => (i.Text, i.Order)).ToList();
            list.Delete(IdOf(list, "U1"));
            var after = list.Items.Where(i => i.IsCompleted).Select(i => (i.Text, i.Order)).ToList();

            Test.Eq(after.Count, before.Count);
            for (int i = 0; i < before.Count; i++) Test.Eq(after[i], before[i]);
            Test.True(list.Items.All(i => i.Text != "U1"), "被删项不得残留");
            Test.Eq(list.Items.Count, 2);
        }),
        ("Delete 未知 Id 抛异常且不修改集合", () =>
        {
            var list = new TodoList(new FakeClock(Base));
            list.Create("A");
            bool threw = false;
            try
            {
                list.Delete(Guid.NewGuid());
            }
            catch (ArgumentException)
            {
                threw = true;
            }
            Test.True(threw, "未知 Id 应抛 ArgumentException");
            Test.Eq(list.Items.Count, 1);
            Test.Eq(list.Items[0].Text, "A");
        }),
        ("置顶项排到未完成组最前，多个置顶按置顶先后", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            list.Create("B");
            list.Create("C");
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "C,B,A");

            list.Pin(IdOf(list, "A"));
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "A,C,B");
            list.Pin(IdOf(list, "B"));
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "A,B,C", "先置顶的排在前");
            Test.Eq(list.Items.Where(i => i.IsPinned).Select(i => i.PinOrder).ToArray(), new[] { 0, 1 });
        }),
        ("取消置顶回到普通区开头", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            list.Create("B");
            list.Create("C");
            list.Create("D");
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "D,C,B,A");
            list.Pin(IdOf(list, "A"));
            list.Pin(IdOf(list, "B"));
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "A,B,D,C");

            list.Unpin(IdOf(list, "A"));
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "B,A,D,C", "取消置顶后落到普通区开头");
            Test.Eq(list.Items.Where(i => i.IsPinned).Select(i => i.Text).ToArray(), new[] { "B" });
        }),
        ("新建事项插到普通区开头，不越过置顶项", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            list.Create("B");
            list.Pin(IdOf(list, "A"));
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "A,B");

            list.Create("C");
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "A,C,B");
        }),
        ("置顶项完成后进已完成组首位，再取消完成回到置顶区", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            list.Create("B");
            list.Create("C");
            list.Pin(IdOf(list, "A"));
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "A,C,B");

            clock.UtcNow = Base.AddMinutes(5);
            list.SetCompleted(IdOf(list, "A"), true);
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "C,B,A", "与普通事项一致：进已完成组首位");
            Test.Eq(Item(list, "A").IsCompleted, true);

            list.Pin(IdOf(list, "A"));
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "C,B,A", "已完成事项不可置顶");

            clock.UtcNow = Base.AddMinutes(9);
            list.SetCompleted(IdOf(list, "A"), false);
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "A,C,B", "仍是置顶项，回到置顶区首位");
        }),
        ("置顶项与普通项的拖动被夹取在各自子区内", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            list.Create("B");
            list.Create("C");
            list.Create("D");
            list.Pin(IdOf(list, "A"));
            list.Pin(IdOf(list, "B"));
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "A,B,D,C");

            list.Move(IdOf(list, "A"), 5);      // 置顶项越界 -> 夹到置顶区末尾
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "B,A,D,C");
            list.Move(IdOf(list, "D"), 0);      // 普通项不得进入置顶区
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "B,A,D,C");
            list.Move(IdOf(list, "C"), 0);
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "B,A,C,D");
        }),
        ("加载构造：置顶未完成 -> 普通未完成 -> 已完成", () =>
        {
            var items = new[]
            {
                new TodoItem(Guid.NewGuid(), "D", true, 1, Base),
                new TodoItem(Guid.NewGuid(), "N2", false, 1, Base),
                new TodoItem(Guid.NewGuid(), "P2", false, 0, Base, true, 1),
                new TodoItem(Guid.NewGuid(), "C", true, 0, Base),
                new TodoItem(Guid.NewGuid(), "N1", false, 0, Base),
                new TodoItem(Guid.NewGuid(), "P1", false, 2, Base, true, 0),
            };
            var list = new TodoList(items, new FakeClock(Base));
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "P1,P2,N1,N2,C,D");
            Test.Eq(list.Items.Select(i => i.PinOrder).ToArray(), new[] { 0, 1, 0, 0, 0, 0 });
            Test.Eq(list.Items.Select(i => i.Order).ToArray(), new[] { 0, 0, 0, 1, 0, 1 });
        }),
        ("Delete 置顶项后其余置顶项顺序保持", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            list.Create("B");
            list.Create("C");
            list.Pin(IdOf(list, "A"));
            list.Pin(IdOf(list, "B"));
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "A,B,C");

            list.Delete(IdOf(list, "A"));
            Test.Eq(string.Join(",", list.Items.Select(i => i.Text)), "B,C");
            Test.Eq(list.Items[0].IsPinned, true);
            Test.Eq(list.Items[0].PinOrder, 0);
        }),
        ("未完成事项无论多旧都显示", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("久未完成");
            clock.UtcNow = Base.AddDays(400);
            Test.Eq(string.Join(",", list.GetVisible(CompletedRange.Week).Select(i => i.Text)), "久未完成");
        }),
        ("已完成事项超出范围隐藏，等于边界仍显示（一周=7天）", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("未完成");
            list.Create("超过一周");
            list.SetCompleted(IdOf(list, "超过一周"), true);   // LastActiveAt = Base
            clock.UtcNow = Base.AddMinutes(1);
            list.Create("正好一周");
            list.SetCompleted(IdOf(list, "正好一周"), true);   // LastActiveAt = Base+1min

            clock.UtcNow = Base.AddMinutes(1).AddDays(7);
            Test.Eq(
                string.Join(",", list.GetVisible(CompletedRange.Week).Select(i => i.Text)),
                "未完成,正好一周",
                "恰好在 7 天边界仍显示，更早的已完成事项隐藏");
        }),
        ("一个月范围固定为 30 天：第 8 天仍显示，超过 30 天隐藏", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("甲");
            list.SetCompleted(IdOf(list, "甲"), true);   // LastActiveAt = Base
            clock.UtcNow = Base.AddDays(8);
            Test.Eq(string.Join(",", list.GetVisible(CompletedRange.Month).Select(i => i.Text)), "甲");
            clock.UtcNow = Base.AddDays(30).AddSeconds(1);
            Test.Eq(list.GetVisible(CompletedRange.Month).Count, 0, "超过 30 天应隐藏");
        }),
        ("全部范围不做时间过滤并保持分组不变量", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("新未完成");
            clock.UtcNow = Base.AddMinutes(1);
            list.Create("旧未完成");
            list.SetCompleted(IdOf(list, "旧未完成"), true);
            clock.UtcNow = Base.AddDays(400);

            var all = list.GetVisible(CompletedRange.All);
            Test.Eq(string.Join(",", all.Select(i => i.Text)), "新未完成,旧未完成");
            bool seenCompleted = false;
            foreach (var item in all)
            {
                if (item.IsCompleted) seenCompleted = true;
                else Test.Assert(!seenCompleted, "全部范围也必须保持未完成在前");
            }
            Test.Eq(string.Join(",", list.GetVisible(CompletedRange.Week).Select(i => i.Text)), "新未完成");
        }),
        ("GetVisible 过滤不修改全量集合/顺序/时间", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            list.Create("B");
            list.SetCompleted(IdOf(list, "B"), true);
            var before = list.Items.ToList();

            clock.UtcNow = Base.AddDays(10);
            Test.Eq(list.GetVisible(CompletedRange.Week).Count, 1, "超范围的已完成项不出现在返回值中");
            _ = list.GetVisible(CompletedRange.Month);
            _ = list.GetVisible(CompletedRange.All);

            var after = list.Items.ToList();
            Test.Eq(after.Count, before.Count, "全量集合不得因过滤而丢项");
            for (int i = 0; i < before.Count; i++)
            {
                Test.Eq(after[i], before[i]);
            }
        }),
        ("UpdateText 修改文本并刷新 LastActiveAt", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            list.Create("A");
            var id = IdOf(list, "A");
            clock.UtcNow = Base.AddHours(3);
            list.UpdateText(id, "A edited");
            Test.Eq(ById(list, id).Text, "A edited");
            Test.Eq(ById(list, id).LastActiveAt, Base.AddHours(3));
        }),
        ("加载构造规范化为 未完成在前 + 同组按 Order 升序", () =>
        {
            var clock = new FakeClock(Base);
            var activeA = new TodoItem(Guid.NewGuid(), "A", false, 0, Base);
            var activeB = new TodoItem(Guid.NewGuid(), "B", false, 1, Base);
            var doneC = new TodoItem(Guid.NewGuid(), "C", true, 0, Base);
            var doneD = new TodoItem(Guid.NewGuid(), "D", true, 1, Base);
            // 输入乱序：已完成插在中间，物理顺序不代表渲染顺序
            var items = new[] { doneD, activeA, doneC, activeB };
            var list = new TodoList(items, clock);
            var ordered = list.Items;
            Test.Eq(string.Join(",", ordered.Select(i => i.Text)), "A,B,C,D");
            Test.Eq(ordered.Select(i => i.Order).ToArray(), new[] { 0, 1, 0, 1 });
            Test.Eq(ordered[0].IsCompleted, false);
            Test.Eq(ordered[3].IsCompleted, true);
        }),
        ("顺序拖动后同组 Order 重新规范为 0..n-1", () =>
        {
            var clock = new FakeClock(Base);
            var list = new TodoList(clock);
            for (int i = 0; i < 6; i++) list.Create($"T{i}");
            // 新建插入开头，故初始为 T5,T4,T3,T2,T1,T0；把 T5 拖到索引 2
            list.Move(IdOf(list, "T5"), 2);
            var active = list.Items.ToList();
            Test.Eq(string.Join(",", active.Select(i => i.Text)), "T4,T3,T5,T2,T1,T0");
            Test.Eq(active.Select(i => i.Order).ToArray(), new[] { 0, 1, 2, 3, 4, 5 });
        }),
    });

    private static Guid IdOf(TodoList list, string text) => Item(list, text).Id;

    private static TodoItem Item(TodoList list, string text) =>
        list.Items.Single(i => i.Text == text);

    private static TodoItem ById(TodoList list, Guid id) =>
        list.Items.Single(i => i.Id == id);
}

public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset start) => UtcNow = start;
    public DateTimeOffset UtcNow { get; set; }
}
