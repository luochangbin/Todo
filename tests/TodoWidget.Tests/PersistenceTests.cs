using TodoWidget.Core;
using TodoWidget.Persistence;

namespace TodoWidget.Tests;

public static class PersistenceTests
{
    private static readonly DateTimeOffset Base = new(2026, 2, 1, 8, 30, 0, TimeSpan.Zero);

    public static TestSuite Suite() => new("Persistence: JSON 状态仓库", new (string, Action)[]
    {
        ("完整往返保真：事项顺序/状态/Order/时间与设置", () =>
        {
            using var dir = TempDir();
            var repo = new JsonStateRepository(dir.Path);
            var items = new[]
            {
                new TodoItem(Guid.NewGuid(), "买菜", false, 0, Base.AddMinutes(10)),
                new TodoItem(Guid.NewGuid(), "写周报", false, 1, Base.AddHours(1)),
                new TodoItem(Guid.NewGuid(), "已完成的旧事", true, 0, Base.AddDays(-9)),
                new TodoItem(Guid.NewGuid(), "第二件完成", true, 1, Base.AddHours(2)),
                new TodoItem(Guid.NewGuid(), "置顶中", false, 0, Base.AddMinutes(5), true, 0),
            };
            var settings = new AppSettings(
                "#102030",
                "#EEDDCC",
                0.72,
                true,
                "Alt+Q",
                CompletedRange.Month,
                new WindowPlacement(12.5, 340.75, 330, 420, DockEdge.Right, 0.42));
            var save = repo.SaveAsync(new StateSnapshot(items, settings)).GetAwaiter().GetResult();
            Test.True(save.Success, $"保存应成功: {save.Error}");
            var load = repo.LoadAsync().GetAwaiter().GetResult();
            Test.True(load.Success, $"加载应成功: {load.Error}");
            Test.Eq(load.FileMissing, false);
            Test.Eq(load.Items.Count, items.Length);
            for (int i = 0; i < items.Length; i++)
                Test.Eq(load.Items[i], items[i], $"第 {i} 项应逐字段相等");
            Test.Eq(load.Settings, settings, "设置应逐字段相等");
        }),
        ("隐藏的历史事项仍随完整状态落盘并重新加载", () =>
        {
            using var dir = TempDir();
            var repo = new JsonStateRepository(dir.Path);
            var items = new[]
            {
                new TodoItem(Guid.NewGuid(), "最近事项", false, 0, Base),
                new TodoItem(Guid.NewGuid(), "7天前的事", false, 1, Base.AddDays(-8)),
            };
            Test.True(repo.SaveAsync(new StateSnapshot(items, AppSettings.Default)).GetAwaiter().GetResult().Success);
            var load = repo.LoadAsync().GetAwaiter().GetResult();
            Test.Eq(load.Items.Count, 2, "超过 7 天的历史项也必须保存在数据文件中");
            Test.Eq(load.Items[1].Text, "7天前的事");
        }),
        ("文件缺失时返回默认空状态且不创建文件", () =>
        {
            using var dir = TempDir();
            var repo = new JsonStateRepository(dir.Path);
            var load = repo.LoadAsync().GetAwaiter().GetResult();
            Test.True(load.Success);
            Test.True(load.FileMissing);
            Test.Eq(load.Items.Count, 0);
            Test.Eq(load.Settings, AppSettings.Default);
            Test.True(!File.Exists(repo.FilePath), "Load 不应自行创建状态文件");
        }),
        ("保存为原子替换：正式文件有效且无 .tmp 残留", () =>
        {
            using var dir = TempDir();
            var repo = new JsonStateRepository(dir.Path);
            var items = new[] { new TodoItem(Guid.NewGuid(), "A", false, 0, Base) };
            for (int i = 0; i < 5; i++)
            {
                var r = repo.SaveAsync(new StateSnapshot(items, AppSettings.Default)).GetAwaiter().GetResult();
                Test.True(r.Success);
            }
            Test.Eq(Directory.GetFiles(dir.Path, "*.tmp").Length, 0, "不得遗留临时文件");
            Test.Eq(Directory.GetFiles(dir.Path, "state.json").Length, 1);
            var raw = File.ReadAllText(repo.FilePath);
            Test.True(raw.Contains("\"SchemaVersion\":1"), "根对象应含 SchemaVersion:1");
            Test.True(raw.Contains("\"Todos\""), "根对象应含 Todos");
            Test.True(raw.Contains("\"Settings\""), "根对象应含 Settings");
            var load = repo.LoadAsync().GetAwaiter().GetResult();
            Test.True(load.Success);
            Test.Eq(load.Items.Count, 1);
        }),
        ("损坏 JSON：读取失败保留原文件，保存被拒绝不覆盖", () =>
        {
            using var dir = TempDir();
            var repo = new JsonStateRepository(dir.Path);
            File.WriteAllText(repo.FilePath, "{ 这不是合法JSON ");
            var load = repo.LoadAsync().GetAwaiter().GetResult();
            Test.True(!load.Success, "损坏文件应加载失败");
            Test.True(!string.IsNullOrEmpty(load.Error));
            Test.Eq(load.Items.Count, 0);
            Test.Eq(load.Settings, AppSettings.Default);
            var original = File.ReadAllBytes(repo.FilePath);
            var save = repo.SaveAsync(new StateSnapshot(
                new[] { new TodoItem(Guid.NewGuid(), "新事项", false, 0, Base) },
                AppSettings.Default)).GetAwaiter().GetResult();
            Test.True(!save.Success, "损坏文件不应被空状态覆盖");
            Test.True(original.SequenceEqual(File.ReadAllBytes(repo.FilePath)), "原损坏文件字节必须保持不变");
        }),
        ("未来版本 SchemaVersion>1：不加载、禁止覆盖原文件", () =>
        {
            using var dir = TempDir();
            var repo = new JsonStateRepository(dir.Path);
            const string future = """{"SchemaVersion":2,"Todos":[],"Settings":{}}""";
            File.WriteAllText(repo.FilePath, future);
            var load = repo.LoadAsync().GetAwaiter().GetResult();
            Test.True(!load.Success, "高版本文件应拒绝加载");
            Test.True(load.UnsupportedVersion);
            var save = repo.SaveAsync(new StateSnapshot(Array.Empty<TodoItem>(), AppSettings.Default)).GetAwaiter().GetResult();
            Test.True(!save.Success, "不得用低版本 Schema 覆盖未来版本文件");
            Test.Eq(File.ReadAllText(repo.FilePath), future);
        }),
        ("缺失可选字段回退默认设置（旧文件/半写文件兼容）", () =>
        {
            using var dir = TempDir();
            var repo = new JsonStateRepository(dir.Path);
            const string minimal = """
                {"SchemaVersion":1,
                 "Todos":[
                   {"Id":"11111111-1111-1111-1111-111111111111","Text":"只有必填","IsCompleted":false,"Order":0,"LastActiveAt":"2026-02-01T08:30:00Z"}
                 ],
                 "Settings":{"BackgroundColor":"#111111","ShowHistory":true}}
                """;
            File.WriteAllText(repo.FilePath, minimal);
            var load = repo.LoadAsync().GetAwaiter().GetResult();
            Test.True(load.Success, $"应能加载: {load.Error}");
            Test.Eq(load.Items.Count, 1);
            Test.Eq(load.Settings.BackgroundColor, "#111111");
            Test.Eq(load.Settings.TextColor, AppSettings.Default.TextColor, "缺失字体颜色应回退默认");
            Test.Eq(load.Items[0].IsPinned, false, "旧文件缺少置顶字段应视为未置顶");
            Test.Eq(load.Items[0].PinOrder, 0);
            Test.Eq(load.Settings.CompletedRange, CompletedRange.All,
                "旧 ShowHistory=true 曾表示展示全部，应映射为 All 以免可见性丢失");
            Test.Eq(load.Settings.Opacity, AppSettings.MaxOpacity, "缺失不透明度应回退 100%");
            Test.Eq(load.Settings.AlwaysOnTop, false, "缺失置顶应回退 false");
            Test.Eq(load.Settings.ToggleHotkey, AppSettings.Default.ToggleHotkey, "缺失快捷键应回退默认");
            Test.Eq(AppSettings.Default.ToggleHotkey, "Alt+Q");
            Test.Eq(load.Settings.Window, AppSettings.Default.Window, "缺失窗口放置应回退默认");
        }),
        ("显示范围读取：旧 ShowHistory=false 等价一周，全新文件用默认", () =>
        {
            using var dir = TempDir();
            var repo = new JsonStateRepository(dir.Path);
            const string legacyHidden = """
                {"SchemaVersion":1,"Todos":[],
                 "Settings":{"BackgroundColor":"#111111","ShowHistory":false}}
                """;
            File.WriteAllText(repo.FilePath, legacyHidden);
            var legacy = repo.LoadAsync().GetAwaiter().GetResult();
            Test.True(legacy.Success, $"应能加载: {legacy.Error}");
            Test.Eq(legacy.Settings.CompletedRange, CompletedRange.Week, "旧 ShowHistory=false 等价旧的 7 天窗口");

            var freshRepo = new JsonStateRepository(Path.Combine(dir.Path, "fresh"));
            var fresh = freshRepo.LoadAsync().GetAwaiter().GetResult();
            Test.Eq(fresh.Settings.CompletedRange, CompletedRange.Week, "文件缺失时用默认范围");
        }),
        ("显式 CompletedRange 优先于同存于文件中的旧 ShowHistory", () =>
        {
            using var dir = TempDir();
            var repo = new JsonStateRepository(dir.Path);
            const string bothFields = """
                {"SchemaVersion":1,"Todos":[],
                 "Settings":{"BackgroundColor":"#111111","ShowHistory":true,"CompletedRange":"Week"}}
                """;
            File.WriteAllText(repo.FilePath, bothFields);
            var load = repo.LoadAsync().GetAwaiter().GetResult();
            Test.True(load.Success, $"应能加载: {load.Error}");
            Test.Eq(load.Settings.CompletedRange, CompletedRange.Week, "新字段存在时应忽略旧字段");
        }),
        ("不透明度越界被夹取到 [0.35, 1.0]", () =>
        {
            using var dir = TempDir();
            var repo = new JsonStateRepository(dir.Path);

            File.WriteAllText(repo.FilePath, """
                {"SchemaVersion":1,"Todos":[],"Settings":{"BackgroundColor":"#111111","Opacity":0.01}}
                """);
            var low = repo.LoadAsync().GetAwaiter().GetResult();
            Test.True(low.Success, $"应能加载: {low.Error}");
            Test.Eq(low.Settings.Opacity, AppSettings.MinOpacity, "过低应夹取到 0.35，避免窗口不可见");

            File.WriteAllText(repo.FilePath, """
                {"SchemaVersion":1,"Todos":[],"Settings":{"BackgroundColor":"#111111","Opacity":3}}
                """);
            var high = repo.LoadAsync().GetAwaiter().GetResult();
            Test.True(high.Success, $"应能加载: {high.Error}");
            Test.Eq(high.Settings.Opacity, AppSettings.MaxOpacity, "过高应夹取到 1.0");

            Test.Eq(AppSettings.ClampOpacity(double.NaN), AppSettings.MaxOpacity, "NaN 应回退 100%");
            Test.Eq(AppSettings.ClampOpacity(0.6), 0.6, "区间内的值应原样保留");
        }),
        ("无背景色识别：none 大小写与空白都算，普通色与空值不算", () =>
        {
            Test.True(AppSettings.IsNoBackground("none"));
            Test.True(AppSettings.IsNoBackground(" NONE "));
            Test.True(AppSettings.IsNoBackground(AppSettings.NoBackgroundColor));
            Test.True(!AppSettings.IsNoBackground("#1B1C20"));
            Test.True(!AppSettings.IsNoBackground(""));
            Test.True(!AppSettings.IsNoBackground(null));
            Test.Eq(AppSettings.Default.BackgroundColor, "#1B1C20", "默认仍是具体颜色");
        }),
        ("保存的文件里中文原样可读（不再转义成 \\uXXXX）", () =>
        {
            using var dir = TempDir();
            var repo = new JsonStateRepository(dir.Path);
            var items = new[]
            {
                new TodoItem(Guid.NewGuid(), "买菜 · 未完成", false, 0, Base),
                new TodoItem(Guid.NewGuid(), "已完成【一项】", true, 0, Base),
            };
            Test.True(repo.SaveAsync(new StateSnapshot(items, AppSettings.Default)).GetAwaiter().GetResult().Success);

            string text = File.ReadAllText(repo.FilePath);
            Test.True(text.Contains("买菜"), "中文应以原字符写入，便于人工查看与编辑");
            Test.True(text.Contains("已完成【一项】"));
            Test.Assert(!text.Contains("\\u4e70", StringComparison.OrdinalIgnoreCase), "不应再出现中文的 \\uXXXX 转义");
            Test.True(text.Contains("#1B1C20"), "设置项同样可读");

            var load = repo.LoadAsync().GetAwaiter().GetResult();
            Test.True(load.Success, $"应能读回: {load.Error}");
            Test.Eq(load.Items[0].Text, "买菜 · 未完成", "放宽编码不得影响往返保真");
            Test.Eq(load.Items[1].Text, "已完成【一项】");
            Test.Eq(load.Items[1].IsCompleted, true);
        }),
        ("并发保存串行化：结果始终等于某次完整快照", () =>
        {
            using var dir = TempDir();
            var repo = new JsonStateRepository(dir.Path);
            var snapshots = Enumerable.Range(0, 8)
                .Select(k => new StateSnapshot(
                    Enumerable.Range(0, 12)
                        .Select(j => new TodoItem(Guid.NewGuid(), $"S{k}-#{j}", j % 2 == 0, j, Base.AddMinutes(k)))
                        .ToList(),
                    AppSettings.Default with { BackgroundColor = $"#{k:D6}" }))
                .ToArray();
            var tasks = snapshots.Select(s => repo.SaveAsync(s)).ToArray();
            Task.WaitAll(tasks);
            var load = repo.LoadAsync().GetAwaiter().GetResult();
            Test.True(load.Success);
            bool matchesAny = snapshots.Any(s =>
                s.Settings == load.Settings
                && s.Items.SequenceEqual(load.Items));
            Test.True(matchesAny, "文件必须是某一次保存的完整快照，不允许两批数据混杂");
            Test.Eq(Directory.GetFiles(dir.Path, "*.tmp").Length, 0);
        }),
        ("数据目录不可写时保存失败并返回错误", () =>
        {
            var blockedPath = Path.Combine(Path.GetTempPath(), $"tw-blocked-{Guid.NewGuid():N}.txt");
            File.WriteAllText(blockedPath, "blocked");
            try
            {
                var repo = new JsonStateRepository(blockedPath);
                var save = repo.SaveAsync(new StateSnapshot(Array.Empty<TodoItem>(), AppSettings.Default)).GetAwaiter().GetResult();
                Test.True(!save.Success, "目录不可写时保存必须失败");
                Test.True(!string.IsNullOrEmpty(save.Error));
            }
            finally
            {
                File.Delete(blockedPath);
            }
        }),
    });

    private static TempDirectory TempDir() => new();

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tw-persist-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }
}
