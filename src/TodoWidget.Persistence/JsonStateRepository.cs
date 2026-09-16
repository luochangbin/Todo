using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using TodoWidget.Core;

namespace TodoWidget.Persistence;

public sealed class JsonStateRepository
{
    public const int SupportedSchemaVersion = 1;
    private const string FileName = "state.json";

    // 默认编码器会把所有非 ASCII 转义成 \uXXXX（文件可读性差）。放宽到全部 Unicode 范围，
    // 让中文等字符原样写入，便于人工查看与编辑；HTML 敏感字符（< > & " '）仍保持转义。
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dataDirectory;
    private bool _fileBlocked;

    public JsonStateRepository(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    public string FilePath => Path.Combine(_dataDirectory, FileName);

    public async Task<StateLoadResult> LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!File.Exists(FilePath))
            {
                _fileBlocked = false;
                return new StateLoadResult(true, true, false, Array.Empty<TodoItem>(), AppSettings.Default, null);
            }

            StateLoadResult result;
            string text;
            try
            {
                text = File.ReadAllText(FilePath);
            }
            catch (Exception e)
            {
                result = Failed($"无法读取状态文件: {e.Message}");
                _fileBlocked = true;
                return result;
            }

            try
            {
                result = ParseDocument(text);
            }
            catch (Exception e)
            {
                result = Failed($"状态文件损坏或格式无效，已保留原文件: {e.Message}");
            }

            if (!result.Success) _fileBlocked = true;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StateSaveResult> SaveAsync(StateSnapshot snapshot)
    {
        await _gate.WaitAsync();
        string? tempPath = null;
        try
        {
            if (_fileBlocked)
            {
                return new StateSaveResult(false,
                    "检测到损坏或更新版本的状态文件，已保留原文件，本次修改未保存。");
            }

            Directory.CreateDirectory(_dataDirectory);

            // 序列化留在调用线程（要读快照），真正耗时的写盘 + fsync + 原子替换挪到线程池。
            // 这段原先全在调用线程（对 UI 来说是 UI 线程）上同步执行：await _gate.WaitAsync()
            // 在闸门空闲时同步完成、不会让出线程，于是一次 fsync 就能把界面卡住几十毫秒。
            var json = BuildDocument(snapshot).ToJsonString(JsonOptions);
            tempPath = FilePath + ".tmp";
            var writePath = tempPath;
            var targetPath = FilePath;
            // 库层不强制回到调用方的上下文：否则在 UI 线程上同步等待保存的调用方会死锁
            // （续体要回 UI 线程，UI 线程却在等它）。UI 侧的 await 仍在自己的上下文里恢复。
            await Task.Run(() =>
            {
                using (var stream = new FileStream(writePath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(targetPath))
                {
                    File.Replace(writePath, targetPath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(writePath, targetPath);
                }
            }).ConfigureAwait(false);
            tempPath = null;
            return new StateSaveResult(true, null);
        }
        catch (Exception e)
        {
            return new StateSaveResult(false, $"保存失败，修改仍在内存中: {e.Message}");
        }
        finally
        {
            if (tempPath != null)
            {
                try { File.Delete(tempPath); } catch { /* best effort */ }
            }
            _gate.Release();
        }
    }

    private StateLoadResult Failed(string message) =>
        new(false, false, false, Array.Empty<TodoItem>(), AppSettings.Default, message);

    private static StateLoadResult ParseDocument(string text)
    {
        var root = JsonNode.Parse(text) as JsonObject
            ?? throw new InvalidDataException("根节点不是 JSON 对象");

        int version = ReadInt(root, "SchemaVersion")
            ?? throw new InvalidDataException("缺少 SchemaVersion");
        if (version != SupportedSchemaVersion)
        {
            if (version > SupportedSchemaVersion)
            {
                return new StateLoadResult(
                    false, false, true, Array.Empty<TodoItem>(), AppSettings.Default,
                    $"状态文件来自更新版本 (SchemaVersion={version})，已保留原文件。");
            }
            throw new InvalidDataException($"不支持的 SchemaVersion={version}");
        }

        var todos = ParseTodos(root);
        var settings = ReadSettings(root["Settings"]);
        return new StateLoadResult(true, false, false, todos, settings, null);
    }

    private static List<TodoItem> ParseTodos(JsonObject root)
    {
        var node = root["Todos"];
        if (node is null) throw new InvalidDataException("缺少 Todos");
        if (node is not JsonArray array) throw new InvalidDataException("Todos 不是数组");

        var result = new List<TodoItem>(array.Count);
        foreach (var entry in array)
        {
            if (entry is not JsonObject o)
                throw new InvalidDataException("Todo 项不是对象");
            var id = Guid.TryParse(ReadString(o, "Id"), out var g)
                ? g
                : throw new InvalidDataException("Todo 缺少合法 Id");
            string? text = ReadString(o, "Text")
                ?? throw new InvalidDataException("Todo 缺少 Text");
            bool completed = ReadBool(o, "IsCompleted") ?? false;
            int order = ReadInt(o, "Order") ?? 0;
            var lastActive = ReadDateTimeOffset(o, "LastActiveAt")
                ?? throw new InvalidDataException("Todo 缺少 LastActiveAt");
            bool pinned = ReadBool(o, "IsPinned") ?? false;
            int pinOrder = ReadInt(o, "PinOrder") ?? 0;
            result.Add(new TodoItem(id, text, completed, order, lastActive, pinned, pinOrder));
        }
        return result;
    }

    private static AppSettings ReadSettings(JsonNode? node)
    {
        var defaults = AppSettings.Default;
        if (node is not JsonObject o) return defaults;

        return new AppSettings(
            ReadString(o, "BackgroundColor") ?? defaults.BackgroundColor,
            ReadString(o, "TextColor") ?? defaults.TextColor,
            AppSettings.ClampOpacity(ReadDouble(o, "Opacity") ?? defaults.Opacity),
            ReadBool(o, "AlwaysOnTop") ?? defaults.AlwaysOnTop,
            ReadToggleHotkey(o),
            ReadCompletedRange(o),
            ReadWindow(o["Window"]),
            ReadBool(o, "AutoStartConfigured") ?? defaults.AutoStartConfigured);
    }

    // 旧格式用 ShowHotkey / MinimizeHotkey 两个字段，现合并为单一 ToggleHotkey：
    // 缺失新字段时优先沿用非空的旧值，都没有（含旧文件留空表示禁用）则使用默认 Alt+Q。
    private static string ReadToggleHotkey(JsonObject o)
    {
        string? value = ReadString(o, "ToggleHotkey");
        if (value is not null) return value;

        string? show = ReadString(o, "ShowHotkey");
        if (!string.IsNullOrWhiteSpace(show)) return show;

        string? minimize = ReadString(o, "MinimizeHotkey");
        if (!string.IsNullOrWhiteSpace(minimize)) return minimize;

        return AppSettings.Default.ToggleHotkey;
    }

    // 新字段缺失时兼容旧格式：ShowHistory=true 曾表示“展示全部”，映射为 All，
    // 避免升级后原本可见的旧已完成事项凭空消失；false 等价于旧的 7 天窗口。
    private static CompletedRange ReadCompletedRange(JsonObject o)
    {
        if (TryReadEnum(o, "CompletedRange", out CompletedRange parsed)) return parsed;
        bool? legacyShowHistory = ReadBool(o, "ShowHistory");
        if (legacyShowHistory is not null)
        {
            return legacyShowHistory.Value ? CompletedRange.All : CompletedRange.Week;
        }
        return AppSettings.Default.CompletedRange;
    }

    private static bool TryReadEnum<T>(JsonObject o, string key, out T value) where T : struct, Enum
    {
        value = default;
        try
        {
            if (o.TryGetPropertyValue(key, out var node) && node is JsonValue jsonValue
                && jsonValue.TryGetValue<string>(out var s)
                && Enum.TryParse(s, out T parsed))
            {
                value = parsed;
                return true;
            }
        }
        catch { /* tolerant */ }
        return false;
    }

    private static WindowPlacement ReadWindow(JsonNode? node)
    {
        var defaults = AppSettings.Default.Window;
        if (node is not JsonObject o) return defaults;

        return new WindowPlacement(
            ReadDouble(o, "Left") ?? defaults.Left,
            ReadDouble(o, "Top") ?? defaults.Top,
            ReadDouble(o, "Width") ?? defaults.Width,
            ReadDouble(o, "Height") ?? defaults.Height,
            ReadDockEdge(o["DockEdge"]) ?? defaults.DockEdge,
            ReadDouble(o, "Anchor") ?? defaults.Anchor);
    }

    private static DockEdge? ReadDockEdge(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        try
        {
            return value.TryGetValue<string>(out var s) && Enum.TryParse<DockEdge>(s, out var edge)
                ? edge
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadString(JsonObject o, string key)
    {
        try
        {
            if (o.TryGetPropertyValue(key, out var node) && node is JsonValue value
                && value.TryGetValue<string>(out var s))
                return s;
        }
        catch { /* tolerant */ }
        return null;
    }

    private static bool? ReadBool(JsonObject o, string key)
    {
        try
        {
            if (o.TryGetPropertyValue(key, out var node) && node is JsonValue value
                && value.TryGetValue<bool>(out var b))
                return b;
        }
        catch { /* tolerant */ }
        return null;
    }

    private static int? ReadInt(JsonObject o, string key)
    {
        try
        {
            if (o.TryGetPropertyValue(key, out var node) && node is JsonValue value
                && value.TryGetValue<int>(out var i))
                return i;
        }
        catch { /* tolerant */ }
        return null;
    }

    private static double? ReadDouble(JsonObject o, string key)
    {
        try
        {
            if (o.TryGetPropertyValue(key, out var node) && node is JsonValue value
                && value.TryGetValue<double>(out var d))
                return d;
        }
        catch { /* tolerant */ }
        return null;
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonObject o, string key)
    {
        try
        {
            if (o.TryGetPropertyValue(key, out var node) && node is JsonValue value
                && value.TryGetValue<DateTimeOffset>(out var t))
                return t;
            if (o.TryGetPropertyValue(key, out var raw) && raw is JsonValue strValue
                && strValue.TryGetValue<string>(out var s)
                && DateTimeOffset.TryParse(s, out var parsed))
                return parsed;
        }
        catch { /* tolerant */ }
        return null;
    }

    private static JsonObject BuildDocument(StateSnapshot snapshot)
    {
        var todos = new JsonArray();
        foreach (var item in snapshot.Items)
        {
            todos.Add(new JsonObject
            {
                ["Id"] = item.Id.ToString("D"),
                ["Text"] = item.Text,
                ["IsCompleted"] = item.IsCompleted,
                ["Order"] = item.Order,
                ["LastActiveAt"] = item.LastActiveAt.ToString("O"),
                ["IsPinned"] = item.IsPinned,
                ["PinOrder"] = item.PinOrder,
            });
        }

        var s = snapshot.Settings;
        var window = s.Window;
        return new JsonObject
        {
            ["SchemaVersion"] = SupportedSchemaVersion,
            ["Todos"] = todos,
            ["Settings"] = new JsonObject
            {
                ["BackgroundColor"] = s.BackgroundColor,
                ["TextColor"] = s.TextColor,
                ["Opacity"] = s.Opacity,
                ["AlwaysOnTop"] = s.AlwaysOnTop,
                ["ToggleHotkey"] = s.ToggleHotkey,
                ["AutoStartConfigured"] = s.AutoStartConfigured,
                ["CompletedRange"] = s.CompletedRange.ToString(),
                ["Window"] = new JsonObject
                {
                    ["Left"] = window.Left,
                    ["Top"] = window.Top,
                    ["Width"] = window.Width,
                    ["Height"] = window.Height,
                    ["DockEdge"] = window.DockEdge.ToString(),
                    ["Anchor"] = window.Anchor,
                },
            },
        };
    }
}
