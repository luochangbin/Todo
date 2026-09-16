# AR-001 设计

## ADDED Design Sections（模块：todo-desktop）

### Design: 技术边界与项目结构

交付物采用单进程 Windows WPF（Windows Presentation Foundation）桌面应用，目标框架固定为 `net10.0-windows`，首个发布 RID 固定为 `win-x64`。本机已验证存在 .NET SDK 10.0.400 和 Windows Desktop Runtime 10.0.11；发布使用 self-contained 单文件方式，用户解压后直接运行，不需要安装运行时。

项目结构：

```text
TodoWidget.sln
src/
  TodoWidget.Core/          # 事项模型、状态机、排序、历史过滤
  TodoWidget.Persistence/   # JSON 状态文件、原子保存、数据错误保护
  TodoWidget.Desktop/       # WPF 窗口、设置、Win32 停靠和全局快捷键
tests/
  TodoWidget.Tests/         # 无第三方测试框架的可执行回归测试
```

依赖方向固定为 `Desktop -> Core/Persistence`、`Persistence -> Core`。Core 不引用 WPF 或 Win32；所有“完成项不能位于未完成项前面”的规则由 Core 统一维护，不散落在窗口事件处理器中。

本 AR 不引入联网、数据库、账号、托盘、通知、搜索、标签、重复任务、安装器或自动启动。桌面窗口只创建一个 `MainWindow` 实例。

### Design: 领域模型与不变量

Core 对外提供以下模型和服务：

```csharp
public sealed record TodoItem(
    Guid Id,
    string Text,
    bool IsCompleted,
    int Order,
    DateTimeOffset LastActiveAt);

public enum DockEdge { None, Left, Right, Top, Bottom }

public sealed record WindowPlacement(
    double Left, double Top, double Width, double Height,
    DockEdge DockEdge, double Anchor);

public sealed record AppSettings(
    string BackgroundColor,
    string ShowHotkey,
    string MinimizeHotkey,
    bool ShowHistory,
    WindowPlacement Window);
```

事项身份永远使用 `Id`，不使用数组索引。`Order` 只表示同一完成状态组内的顺序；渲染顺序恒为未完成组按 Order 升序，再接已完成组按 Order 升序。

`TodoList` 提供 `Create`、`UpdateText`、`SetCompleted`、`Move` 和 `GetVisible`。状态迁移规则：

- 未完成勾选为完成：更新时间，移入已完成组首位，其余已完成项顺序保持。
- 已完成取消勾选：更新时间，移入未完成组首位，其余未完成项顺序保持。
- 同组拖动只重排源组，并重新规范该组 Order。
- 跨组拖动不改变 IsCompleted：拖动已完成项到未完成区域时限制到已完成组合法位置；拖动未完成项到已完成区域时限制到未完成组合法位置。
- 编辑、完成/取消完成、创建和有效拖动更新 `LastActiveAt`；查看、启动和显示历史不更新。

历史规则为 `LastActiveAt < clock.UtcNow - TimeSpan.FromDays(7)` 时默认隐藏；等于边界仍显示。过滤只作用于 `GetVisible` 返回值，不修改全量集合、完成状态或顺序。

### Design: 持久化格式和一致性

`JsonStateRepository` 将完整状态写入：

```text
%LOCALAPPDATA%\TodoWidget\state.json
```

根对象包含 `SchemaVersion: 1`、`Todos` 和 `Settings`。保存流程是“完整内存快照 -> 同目录临时文件 -> Flush(true) -> 原子替换正式文件”。任何保存失败都保留内存状态并返回错误，不以空状态覆盖正式文件；损坏 JSON 读取失败时保留原文件，使用默认运行时状态并展示错误提示。

Repository 通过 `SemaphoreSlim` 串行化保存，避免排序保存和勾选保存并发时旧快照覆盖新快照。每次保存传入完整快照，不保存“当前可见列表”，因此隐藏的历史事项也始终落盘。

未来版本 `SchemaVersion > 1` 不覆盖原文件；缺失可选字段使用默认设置。事项 ID、完成状态、同组 Order、LastActiveAt、背景色、快捷键、窗口位置和停靠信息都属于持久化数据。

### Design: WPF 窗口与视觉交互

主窗口为无边框、圆角、深色默认背景的 WPF Window，默认尺寸约 `330x420`，标题区域显示 `To Do`、当前可见完成数/总数和设置按钮。事项行包含复选框和文本；已完成文本使用 `TextDecorations=Strikethrough` 和降低不透明度。底部提供事项输入框、添加按钮和“显示历史”复选框。

事项拖动仅在事项行的文本/空白区域启动，复选框、输入框、设置按钮不启动窗口拖动。拖动 UI 只向 Core 提交 `Move`，Core 返回合法顺序后重新渲染；不允许 UI 直接修改 Order。

标题区域调用 WPF 的 `DragMove`。拖动结束后以当前窗口所在显示器的工作区为准，使用 Win32 `MonitorFromWindow`/`GetMonitorInfo` 排除任务栏，若窗口接近任一边缘阈值（内部常量 18px）则：保存完整可见位置，记录 DockEdge 与 Anchor，把窗口收拢为 12px 隐藏点。隐藏点是同一 MainWindow 的缩小状态，不创建第二个窗口；鼠标进入后恢复原尺寸和位置。

启动恢复时验证保存坐标是否仍与任一工作区相交；显示器拔出或坐标越界时回退到主显示器可见区域。窗口位置、停靠方向和锚点随状态保存。

### Design: 设置与全局快捷键

设置窗口包含背景颜色输入/选择、弹出快捷键输入、最小化快捷键输入和保存按钮。快捷键采用可读字符串格式，例如 `Ctrl+Alt+Space`、`Ctrl+Alt+M`。

`HotkeyParser` 负责确定性解析和规范化，只接受至少一个修饰键与一个有效主键。`GlobalHotkeyService` 使用 Win32 `RegisterHotKey`、`UnregisterHotKey` 和 WPF `HwndSource.AddHook` 接收 `WM_HOTKEY`。

配置替换顺序为：解析 -> 检查两个动作内部冲突 -> 尝试注册新组合 -> 新组合成功后注销旧组合 -> 更新内存设置 -> 原子保存。任一步失败都继续使用旧组合和旧持久化设置，并在设置窗口显示“格式无效”或“快捷键已被占用”。启动时单个快捷键注册失败不能阻止事项窗口启动。

弹出快捷键对最小化和边缘隐藏状态执行恢复并激活窗口；最小化快捷键执行 `WindowState=Minimized`。本 AR 不额外保证窗口始终置顶。

### Design: 应用生命周期与错误处理

启动顺序：确定用户数据目录 -> 读取并验证完整状态 -> 创建唯一 MainWindow -> 应用背景色和历史过滤 -> 校正窗口坐标/停靠 -> 尝试注册快捷键 -> 显示窗口。

无法读取或解析状态文件时不覆盖原文件；使用默认状态启动，并在窗口内显示一次可关闭的错误提示。保存失败时当前修改继续留在内存中，提示“修改尚未成功保存”。快捷键冲突只影响对应快捷键，不影响事项列表。

关闭应用时先注销快捷键并等待最后一次保存；自动保存失败不阻塞窗口退出超过短暂等待，正式文件始终优先保留上一份有效内容。

### Design: 测试策略

`TodoWidget.Tests` 是无外部 NuGet 依赖的控制台回归测试，使用可替换 `IClock` 和临时目录，覆盖：

- 默认未完成在前、完成在后；完成/取消完成移动到目标组首位。
- 同组拖动、跨组拖动限制和历史 7 天边界。
- JSON 往返、隐藏历史仍保存、损坏文件不覆盖、原子保存失败保护。
- 快捷键格式解析和应用内冲突判断。

WPF 真实运行验证覆盖发布 exe 启动、唯一窗口、设置窗口、事项勾选/中划线、拖动到边缘形成隐藏点、鼠标进入隐藏点恢复、全局快捷键和重启恢复。由于需求明确是桌面应用，发布产物启动与核心用户结果是必做证据；未额外承诺完整自动化 E2E 框架。

## Delivery Contract

- 交付类型：`desktop-app`
- 启动方式：运行 `publish/win-x64/TodoWidget.Desktop.exe`。
- 发布命令：`dotnet publish src/TodoWidget.Desktop/TodoWidget.Desktop.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true`。
- 最小产物：自包含 Windows x64 可执行文件及其静态资源；不依赖预装 .NET Runtime、安装器、服务或源码目录。
- 用户可观察结果：首次启动出现唯一的可交互 To Do 窗口；事项可添加、编辑、勾选、排序；设置可改背景色和快捷键；窗口可边缘隐藏/弹出；重启后数据和设置恢复。
- 最低证据：Core/Persistence 回归测试 + Release 构建 + 发布目录真实启动冒烟；单元测试不能替代真实桌面启动证据。

## 质询记录

### 维度 1：目标/非目标

- 自述：目标是一个 Windows 单用户、无安装器、可拖动可隐藏、带本地完整持久化的桌面事项组件；联网、通知、托盘、搜索和删除不是本 AR 目标。
- 质询：是否把图片中的具体文字或数量当作需求？→ 结论：不把图片文字当作指令，只采用其深色圆角清单和中划线视觉参考。

### 维度 2：边界与失败模式

- 自述：已覆盖损坏 JSON、保存失败、快捷键冲突、显示器变化和非法拖放；各自降级为保留旧文件/内存、保留旧快捷键、校正坐标或限制到合法组。
- 质询：历史过滤是否可能导致保存时丢失旧事项？→ 结论：保存完整集合快照，不保存派生的可见列表。

### 维度 3：方案取舍

- 自述：选 WPF 而不是 Electron/WinUI，因为本机桌面窗口、Win32 热键、工作区坐标和 self-contained 发布路径短；选 JSON 而不是数据库，因为当前是单用户轻量事项且需免安装。
- 质询：是否需要引入第三方 UI 或测试框架？→ 结论：不需要；首版用 WPF 原生控件和无外部 NuGet 的回归 runner，降低离线构建风险。

### 维度 4：数据流/一致性

- 自述：完整状态是唯一事实源，Core 派生可见列表；状态操作先更新 Core，再生成完整快照并串行原子保存。
- 质询：勾选、排序和历史过滤是否会交叉改变状态？→ 结论：勾选才改变 IsCompleted，排序只改变同组 Order，过滤只改变展示集合。

### 维度 5：测试策略

- 自述：每个规则先由 Core/Persistence 确定性测试覆盖，桌面专有行为由真实发布运行验证覆盖；不虚构未配置的自动化 E2E。
- 质询：只跑组件测试能否证明免安装桌面交付？→ 结论：不能，必须单独执行发布 exe 启动冒烟。

### 维度 6：兼容/迁移

- 自述：这是空仓库 greenfield，没有旧数据格式或公共 API 兼容约束；仍写入 SchemaVersion=1，为未来扩展保留安全拒绝路径。
- 质询：是否需要为旧数据迁移或删除提供不可逆操作？→ 结论：当前不需要；不提供物理删除入口，也不覆盖无法理解的未来版本文件。

## 已知风险

- 用户没有指定最低 Windows 版本和 ARM 支持范围；首版明确为 Windows 10/11 x64 可执行发布，ARM 和更早系统不在验证范围。
- 边缘阈值、隐藏点尺寸和颜色默认值属于可逆实现参数，首版使用设计中内部常量，不增加设置项。
- Oracle 设计建议了三层模块；为了保持首版实现最小，模块使用三个项目加一个无外部依赖测试 runner，不引入额外框架。
- 全局快捷键在系统层被占用时，应用只能保留旧组合并报告失败，不能夺取其它应用的组合。
