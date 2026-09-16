# 全量设计

## 模块：桌面备忘录（todo-desktop）

（AR-001 归档后合并）


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
    DateTimeOffset LastActiveAt,
    bool IsPinned = false,
    int PinOrder = 0);

public enum DockEdge { None, Left, Right, Top, Bottom }

public sealed record WindowPlacement(
    double Left, double Top, double Width, double Height,
    DockEdge DockEdge, double Anchor);

public enum CompletedRange { Week, Month, All }

public sealed record AppSettings(
    string BackgroundColor,
    string TextColor,
    double Opacity,
    bool AlwaysOnTop,
    string ToggleHotkey,
    CompletedRange CompletedRange,
    WindowPlacement Window);
```

`AppSettings.MinOpacity`/`MaxOpacity` 定义不透明度区间 0.35–1.0，`ClampOpacity` 是唯一的夹取入口（`NaN` 回退 1.0），持久化读取与设置窗口都经它处理。

事项身份永远使用 `Id`，不使用数组索引。`Order` 只表示同一子组内的顺序。物理顺序即渲染顺序，其不变量为：**未完成且置顶（按 `PinOrder` 升序）→ 未完成非置顶（按 `Order` 升序）→ 已完成（按 `Order` 升序）**。`PinOrder` 只在置顶子区内有意义。

`TodoList` 提供 `Create`、`UpdateText`、`SetCompleted`、`Pin`、`Unpin`、`Delete`、`Move` 和 `GetVisible(CompletedRange)`。状态迁移规则：

- 未完成勾选为完成：更新时间，移入已完成组首位，其余已完成项顺序保持（置顶状态不影响已完成组排序）。
- 新建事项：插入普通区开头（最新在上，不越过置顶项），并重新编号。
- 置顶：移到置顶区末尾（多个置顶按置顶先后排列）；已完成事项不可置顶。
- 取消置顶：回到普通区首位。
- 已完成取消勾选：更新时间，回到所在子区首位（置顶项回置顶区首位，普通项回普通区首位）。
- 同子区拖动只重排该子区；置顶项被夹取在置顶区内，普通未完成项被夹取在普通区内。
- 跨组拖动不改变 IsCompleted：拖动已完成项到未完成区域时限制到已完成组合法位置；拖动未完成项到已完成区域时限制到未完成组合法位置。
- 编辑、完成/取消完成、创建、置顶/取消置顶和有效拖动更新 `LastActiveAt`；查看和启动不更新。
- `Delete` 移除指定事项并重新编号；未知 Id 沿用 `FindIndex` 抛 `ArgumentException`，集合保持不变。

显示范围规则：`GetVisible(CompletedRange)` 中未完成事项**不参与时间过滤**，始终返回；已完成事项仅当 `LastActiveAt < clock.UtcNow - 窗口` 时隐藏，等于边界仍显示。窗口为固定天数——`Week` 7 天、`Month` 30 天（不采用“自然月”，保证确定性），`All` 不做时间过滤。过滤只作用于 `GetVisible` 返回值，不修改全量集合、完成状态或顺序。

### Design: 持久化格式和一致性

`JsonStateRepository` 将完整状态写入：

```text
%LOCALAPPDATA%\TodoWidget\state.json
```

根对象包含 `SchemaVersion: 1`、`Todos` 和 `Settings`。保存流程是“完整内存快照 -> 同目录临时文件 -> Flush(true) -> 原子替换正式文件”。任何保存失败都保留内存状态并返回错误，不以空状态覆盖正式文件；损坏 JSON 读取失败时保留原文件，使用默认运行时状态并展示错误提示。

Repository 通过 `SemaphoreSlim` 串行化保存，避免排序保存和勾选保存并发时旧快照覆盖新快照。每次保存传入完整快照，不保存“当前可见列表”，因此超出显示范围的已完成事项也始终落盘。序列化留在调用线程（要读快照），耗时的写盘 + `flushToDisk` fsync + `File.Replace` 原子替换挪到线程池，并 `ConfigureAwait(false)`：这段原先全在调用线程（UI 侧即 UI 线程）上同步执行——`await _gate.WaitAsync()` 在闸门空闲时同步完成、不会让出线程——一次 fsync 就能让界面卡住二十多毫秒；`ConfigureAwait(false)` 同时避免在 UI 线程上同步等待保存的调用方死锁（续体要回 UI 线程、UI 线程却在等它）。

未来版本 `SchemaVersion > 1` 不覆盖原文件；缺失可选字段使用默认设置。`CompletedRange` 以字符串写入，缺失时按旧字段兼容：若同一文件存在历史字段 `ShowHistory`，`true` 映射为 `All`、`false` 映射为 `Week`（旧 `ShowHistory=true` 曾表示展示全部，直接映射为 Week 会让原本可见的旧事项凭空消失）；两者都缺失时使用默认值 `Week`。事项 ID、完成状态、同组 Order、LastActiveAt、置顶标记与置顶顺序、背景色、字体颜色、不透明度、窗口置顶、快捷键、完成事项显示范围、窗口位置、停靠信息和自启动标记都属于持久化数据。

### Design: 托盘图标与随系统启动

主窗口 `ShowInTaskbar=False`，任务栏与 Alt+Tab 都不出现，因此通知区域（托盘）是唯一的可见入口：`TrayIcon` 用 WinForms `NotifyIcon`（`<UseWindowsForms>true</UseWindowsForms>`，图标取自 exe 自身的 `app.ico`），双击切换显示/隐藏，右键菜单（WinForms `ContextMenuStrip`，保持系统原生外观）提供“显示 / 隐藏”与“退出”。引入 WinForms 会带来 `System.Windows.Forms`/`System.Drawing` 的隐式 using，与 WPF 的 `Application`/`Color`/`Button` 同名冲突，因此 csproj 里用 `<Using Remove="..."/>` 移除这两个隐式 using，只在 `TrayIcon.cs` 内显式别名引用。

随系统启动写当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`（无需管理员权限），值名为 `TodoWidget`、内容为带引号的 `Environment.ProcessPath`。开启时机由持久化标记 `AppSettings.AutoStartConfigured` 控制：为 `false`（首次运行，或旧状态文件缺少该字段）时登记启动项并立刻把标记置为 `true` 落盘；这样只登记一次，用户在系统设置里关掉启动项后不会被应用反复打开。从托盘菜单“退出”会先删除启动项，再走正常的收尾保存与退出流程（`FinalCloseAsync` 里会 `Dispose` 掉托盘图标与快捷键）。

### Design: WPF 窗口与视觉交互

主窗口为无边框的 WPF Window，默认尺寸约 `330x420`，默认背景色 `#1B1C20`、默认字体色 `#E9EAEE`。界面字体固定为微软雅黑（`Microsoft YaHei UI, Microsoft YaHei, Segoe UI`），不提供字体设置。窗口 `ShowInTaskbar="False"`，因此不占任务栏、也不出现在 Alt+Tab 列表里，只靠全局快捷键唤出；可执行文件通过 `<ApplicationIcon>..\..\todo.ico</ApplicationIcon>` 内嵌应用图标——图标直接使用仓库根目录的 `todo.ico`（9 档：16/20/24/32/40/48/64/128/256，全部 32bpp PNG 带透明）。托盘图标复用同一个 exe 图标（`Icon.ExtractAssociatedIcon(Environment.ProcessPath)`），因此换图标只需替换 `todo.ico`。仓库根的 `logo.png` 与临时工具 `IconTool`（从四周泛洪去白底 + 按 alpha 质量裁到内容外接框 + 打包多档 ICO）保留着，需要用位图生成 ICO 时可以复用。 **圆角约定：窗口、设置窗口、删除确认气泡、事项行、输入框、复选框、滑块轨道、滚动条、提示气泡全部为直角（`CornerRadius=0`）；所有按钮（含图标按钮、图标开关、色块）为 4px 圆角，键盘 focus 环也取 4px 与之匹配。** 标题区自左向右为：`Todo`、当前可见完成数/总数、**新增按钮**（矢量加号）、图钉开关、设置按钮。事项行包含复选框和文本；已完成文本使用 `TextDecorations=Strikethrough` 并改用弱化色。底部不再有输入行。

视觉方向为“现代暗色极简”。设计 token（`Todo.*` 画刷）集中定义在 `App.xaml`，运行时由 `MainWindow.ApplyVisuals()` 按用户背景色重写：窗口底 `Chrome` 恒为用户选色；`Hover`/`Input`/`Separator` 由该底色做明度偏移派生（`ColorUtil.Derive`：暗色向白提亮、明色向黑压暗），纯黑/纯白等极端底色下自动反向，保证与底色始终有可见区分；`Fg`/`Muted`/`Accent`/`Error` 按背景亮度阈值切换固定明暗两套值。字体为 `Segoe UI Variable Text`（回退 `Segoe UI`），正文 13px、标题 14px SemiBold、计数与辅助文案 11/11.5px；控件圆角 6px、窗口圆角 11px。hover 与按下只改变颜色/透明度，不产生位移；focus 环仅在键盘导航时出现。组件层面：复选框 16px（选中为强调色填充 + 白色勾），标题栏与错误条使用 28/20px 图标按钮（矢量 `Path`，不以文字字形充当图标），输入框 32px 并由 `Tag` 提供占位提示，主操作按钮为强调色实心，滚动条为 8px 细条。事项行在悬停时于右侧浮出「编辑」「删除」两个 24px 图标按钮（矢量 `Path`，描边跟随按钮 `Foreground` 悬停变亮）。操作区容器用 `Visibility=Hidden` 而非 `Collapsed`，因此悬停不改变文本宽度、不产生位移，也无需预留固定列宽。删除按钮弹出一个锚定自身的 `Popup` 确认气泡（Popconfirm）：显示「删除这条事项？」、事项文本摘要（单行省略）与取消/删除两个操作，确认按钮用破坏性红色 token（`Todo.DangerBrush`，两套主题共用，白字对比度 ≥4.5:1）；点击气泡外部（`StaysOpen=False`）或按 Esc 关闭。气泡打开时因 `Popup` 是独立顶层窗口，行的 `IsMouseOver` 会变为 false、操作区随之隐藏，但 `Hidden` 仍保留布局边界，`PlacementTarget` 不失效、气泡不跳位。`Render()` 先关闭该气泡，避免重建行后留下悬空 `PlacementTarget`。拖动武装 `_dragArmed` 在窗口 `PreviewMouseLeftButtonUp` 复位，且编辑中不武装拖动；拖动只在位移超过 4px 阈值后才真正开始（`UpdateRowDragArming`），因此单击与双击的第一击都不会进入拖动。

编辑区与内联草稿行统一使用**多行自适应输入**：`TextWrapping=Wrap` + `AcceptsReturn=True` + `MinHeight=26`（单行时与事项行等高）、不设上限，高度随内容增长；Enter 提交、Shift+Enter 换行（`PreviewKeyDown` 拦截 Enter 而不拦截 Shift+Enter）、Esc 取消、失焦提交。底部不再有固定新增行：点击标题栏的加号后，由 `Render()` 在**普通区开头**（`visible.Count(i => i.IsPinned && !i.IsCompleted)` 计算出的槽位）插入一个 `BuildDraftRow()` 生成的内联草稿行，即新事项提交后所在的位置，因此提交时不会发生位置跳变；草稿行不入 `_rowHosts`，不参与拖动与排序计算。

置顶标记：置顶且未完成的行，在操作区之前加入一个 16px 强调色图钉 `Path`（`Todo.PinMark`，`IsHitTestVisible=False`），右对齐、右边距 6px；悬停展开的操作区不透明底会把它盖住，因此不会额外占用宽度。操作区在未完成行上多一个置顶按钮：未置顶时图标为弱化色、点击置顶；已置顶时按钮 `Foreground` 就地设为强调色、点击取消置顶；已完成行不显示该按钮（已完成事项不可置顶）。

字体颜色：用户可选正文色，弱化色由 `ColorUtil.Mix(正文色, 背景色, 0.42)` 派生，保证辅助文案跟随正文色。面板半透明时 `SurfaceFg`/`SurfaceMuted`/`SurfaceAccent` 仍叠加文字 alpha。

**代码创建的视觉元素必须用 `SetResourceReference` 而不是 `FrameworkElement.Foreground = (Brush)Resources[...]`**：后者把属性绑到一个固定画刷实例上，而即时预览只重刷资源、不重建行，已存在的行会保持旧颜色（表现为「设置页文字变了、待办文字不变」）。XAML 里的 `{DynamicResource}` 与代码里的 `SetResourceReference` 才会跟随资源更新。

无背景色：背景色支持特殊值 `none`（`AppSettings.IsNoBackground` 判定；它占用 6 个背景预设色块中的一个——带斜杠的“无背景”，`ColorInput` 也接受直接输入 `none`）。此时 `SurfaceBrush` 与主窗口专用的 `PanelBorderBrush` 都取 `Colors.Transparent`，面板既不填充也不描边；`ChromeBrush` 仍是不透明的派生基准色，因此设置窗口与删除确认气泡不受影响。由于已无用户底色，`Hover`/`Input`/`Separator` 改用默认底色为基准，并按**文字色亮度**选择明暗分支，于是行悬停与输入框仍是可见的半透明灰。

面板半透明：`Todo.SurfaceBrush` = 用户背景色 + `Opacity` 对应的 alpha，只用于主窗口面板；`Hover`/`Input`/`Separator` 派生色同样带上该 alpha，使面板、行 hover、输入框在玻璃态下保持一致。前景色分两层：**主题前景**（`Fg`/`Muted`，以及带面板 alpha 的 `SurfaceFg`/`SurfaceMuted`/`SurfaceAccent`）由面板底色的明暗分支决定，供设置窗口、删除确认气泡、标题栏与各类图标使用；**待办文本前景**（`TextFg`/`TextMuted`）才由用户配置的“待办文字颜色”派生（弱化色 = `Mix(文字色, 底色, 0.42)`），只作用于首页事项文本与行内编辑框、草稿行。这样改文字色不会连带改掉标题栏与设置窗口的文字，其 alpha 由 `TextAlphaFromOpacity` 计算（`0.5 + 0.5 × 面板不透明度`，面板 100% → 100%、面板 35% → 约 67%），让文字随面板同步变淡但保留可读下限。`Todo.ChromeBrush` 与 `Fg`/`Muted`/`Accent`/`Danger*` 保持不透明，供设置窗口与确认气泡使用（保证调设置、确认删除时文字清晰）；`ToolTip` 也改用不透明底色。明暗判定仍用不透明底色的亮度，不透明度量只影响 alpha。

主窗口标题栏自左向右为：标题、完成计数、**图钉开关**（`ToggleButton` + `Todo.IconToggle`/`Todo.ToggleGlyph`，手写 16px 图钉几何，选中态描边取强调色）、设置按钮。点击图钉即切换 `Window.Topmost` 并持久化 `AlwaysOnTop`。标题栏拖动判定由 `HasAncestor<Button>` 放宽为 `HasAncestor<ButtonBase>`，否则点击图钉会连带触发窗口拖动。底部固定新增行的加号在整行水平居中；进入草稿态后输入框占满整行。

事项双击编辑与拖动排序都判定在**事项行宿主**（`Border`）上，不在文本元素上：双击行内任意非交互区域（文本、行内空白）都进入编辑，文本右侧的空白与悬停浮出的图标区因此不再是死区；`IsNonDragSource` 仍按 `OriginalSource` 向上查找 `CheckBox/TextBox/Button` 排除复选框、输入框与图标按钮。行宿主 `PreviewMouseLeftButtonDown` 先判 `e.ClickCount >= 2` 直接进编辑并 `e.Handled=true`，否则只记录拖动候选（`_dragArmed` + 起点），**不在按下时就决定拖动**。

拖动排序不用 OLE `DragDrop.DoDragDrop`：它会让第一击进入模态拖放循环，把双击序列吃掉，使双击编辑失效，也拿不到连续动画。改为手动鼠标捕获 + FLIP 位移动画：窗口级 `PreviewMouseMove` 在左键按下且位移超过 `DragStartPixels`(4) 后才 `StartRowDrag`，记录每行的原始 `Top`/`Height`/`Advance`（`Advance` 取相邻两行的实际间距，因为 WPF `ActualHeight` 不含 `Margin`，直接累加会每行少算间距）并给每行装上 `TranslateTransform`；被拖行用 `Panel.SetZIndex(100)` 抬到最上层、临时套用不透明 `Chrome` 底色与投影并在移动中直接跟手，其余行按"被拖行中心越过谁的中心"算出的目标槽位做 140ms `CubicEase(EaseOut)` 平移让位。松手时先按 Core 的子区规则夹取预览槽位（`ClampDragSlot`，与 `Move` 共用 `TodoList.ClampMoveTarget` 的边界），再把槽位换算成 `Move` 的物理下标——`Move` 是"先移除再插入"，目标下标是移除之后的，落在被拖行原位置之后要减 1，否则会多滑一位。拖动中被系统夺走鼠标捕获（Alt+Tab、窗口隐藏）时在 `LostMouseCapture` 收尾，避免状态卡住。

松手不重建行：`Render()` 重建 34 行实测约 150ms（早期一轮甚至 290ms），是"松手卡一下"的主因，因此改为 `TryReorderRows()` 原地重排——复用已有行元素，只把被拖行在 `RowsPanel.Children` 里挪到"新顺序中紧随其后的那一行"之前（避开草稿行等非行元素的下标干扰），并同步 `_rowHosts` 顺序；复用要求可见集合与当前行完全一致（比较 `GetVisible` 得出的 id 序列），不一致（例如某条已完成事项刚好滑出显示范围）就整体退回 `Render()`。随后 `AnimateDragLanding()` 做落位缓动：重排后布局已落到目标槽位，把被拖行的位移补偿成"松手瞬间它在光标下的位置"（`_dragStartTop + 位移 - _dragTargetTop`）再动画到 0，视觉上就是从光标滑进槽位；其余行的布局位置恰好等于它们动画的终点，位移清零即可、不会跳动（离屏验证里逐行比对了预览位置与落定位置，误差 0）。落位动画结束后要撤销"抬起"的视觉（`Panel.ZIndex`、不透明底、投影）——原地重排不再重建行，不主动清就会留在行上；动画被跳过时立即清。实测松手路径从 210–240ms 降到 4–20ms。

拖动 UI 只向 Core 提交 `Move`，Core 返回合法顺序后再调整显示；不允许 UI 直接修改 Order。

标题区域调用 WPF 的 `DragMove`。拖动结束后以当前窗口所在显示器的工作区为准，使用 Win32 `MonitorFromWindow`/`GetMonitorInfo` 排除任务栏：

- **不贴边**：直接把当前矩形作为完整位置保存。
- **贴边**（距边缘 18px 内，窗口越过边缘也算）：先用 `DockCalculator.ClampIntoWorkArea` 把矩形夹取回工作区，把这个**完整可见**矩形作为恢复位置保存（若直接保存松手时的矩形，恢复后会有一半在屏幕外），记录 DockEdge 与 Anchor，再用 `CollapsedRect` 把窗口整体移出该边缘（保持原尺寸，0 像素可见）。

停靠状态用三个状态描述：`Normal`（浮动）、`Collapsed`（整体移出屏幕）、`ShownFromDock`（因光标触发而滑出显示）。`_edge`/`_anchor` 在停靠后一直保留，`RestoreFromHidden()` 只把窗口移回完整位置、不再清除停靠，因此鼠标离开后能再次收回。

完全移出屏幕后窗口收不到任何鼠标事件，因此 `Collapsed` 期间由 `DockManager` 启动一个 160ms 的 `DispatcherTimer`，用 `GetCursorPos` 取光标位置，交给 `DockCalculator.IsRestoreTrigger` 判定：光标需同时满足「贴住停靠边缘 ±4px」与「落在原停靠位置的范围内」，命中则滑出显示并停止该计时器。

`ShownFromDock` 期间窗口的 `MouseLeave` 会调用 `DockManager.ScheduleCollapse()`，启动一个 420ms 的一次性计时器；触发时若 `_window.IsMouseOver` 仍为假（鼠标没回来）就回到 `Collapsed`。以下情况不收回：左键按下期间（拖动/缩放/排序）、设置窗口打开（`_modalOpen`）、删除确认气泡打开（`DeleteConfirm.IsOpen`）——否则鼠标移到这些浮层上会误收窗口。把窗口拖离边缘释放时 `OnTitleDragEnded` 会把 `_edge` 置回 `None`，即解除停靠。窗口始终是同一个 `MainWindow` 实例。

**拖动改大小**：无边框 + `AllowsTransparency` 的窗口无法依赖系统非客户区缩放（既没有 WS_THICKFRAME，`ResizeMode` 也不可靠），因此自实现——窗口级 `PreviewMouseLeftButtonDown` 判断光标是否落在 `ResizeBand`(6px) 感应带内，命中则记录起始光标屏幕坐标与起始矩形、`CaptureMouse()` 并 `e.Handled=true`（挡掉行拖动）；`PreviewMouseMove` 按位移算出新矩形并按 `MinWidth/MinHeight`(220x160) 夹取；`PreviewMouseLeftButtonUp` 释放捕获并调用 `DockManager.UpdateSizeFromWindow()` 落盘。未拖动时 `PreviewMouseMove` 只根据感应带更新缩放光标。隐藏状态下不参与缩放与光标反馈。屏幕坐标换算用 `PointToScreen` 除以窗口 DPI 缩放，与停靠几何统一为 DIP。

启动恢复时验证保存坐标是否仍与任一工作区相交；显示器拔出或坐标越界时回退到主显示器可见区域。窗口位置、停靠方向和锚点随状态保存。

### Design: 设置与全局快捷键

设置窗口包含背景颜色与字体颜色两组「输入框 + 6 个预设色块」（选中项以强调色描边）、不透明度滑块（极简 `Slider` 模板：4px 轨道 + 强调色已填充段 + 14px 圆滑块 + focus 环，取值 35–100）、完成事项显示范围三选一（最近一周 / 最近一个月 / 全部，圆形 `RadioButton`）、单一全局快捷键输入和保存按钮。设置窗口面板固定使用不透明底色、直角边框。

背景色与字体色走**即时预览**：`MainWindow.PreviewAppearance(背景色, 字体色, 不透明度)` 直接改写运行中窗口的 `_state.Settings` 并 `ApplyVisuals()`（不落盘）。背景色/字体色的 `TextChanged` 与滑块 `ValueChanged` 都汇入 `PreviewFromInputs()`；输入的颜色解析失败时退回当前生效值，避免预览把窗口闪成默认外观。取消、非正常关闭或保存失败时 `RevertPreview()` 回滚为进入设置前的整组外观值，保存成功才持久化。由于设置窗口会完全盖住主窗口，设置窗口改为 `MainWindow.FindDialogPosition` 计算后**贴在主窗口右侧**（右侧放不下则改左侧，再夹取到所在显示器工作区内），否则实时预览不可见。滑块 `ValueChanged` 在 `InitializeComponent` 期间会被 `Minimum`/`Maximum` 的强制取值提前触发，此时 `_owner` 尚未赋值，因此用 `_ready` 标志挡住该次回调。

`HotkeyParser` 负责确定性解析和规范化，只接受至少一个修饰键与一个有效主键。`AppHotkeyRules.ValidateToggleHotkey` 是应用层唯一的格式校验入口（留空视为禁用）。弹出与最小化合并为**单一全局快捷键**（默认 `Alt+Q`，`Alt+Q` 单键同时覆盖「唤出」与「最小化」两种动作）：`GlobalHotkeyService` 因此不再按 Slot 分槽，只维护一个当前注册 id、一个已生效组合与一个回调；`TryApply` 先注册新组合、成功后再注销旧组合，失败则继续沿用旧组合。`MainWindow.OnToggleHotkey` 按当前状态分流，判定收在 `AppHotkeyRules.ShouldRaiseOnHotkey(hidden, isActive, modalOpen, topmost)` 里（便于回归测试）：已隐藏或最小化时恢复并激活；显示中但**不是当前活动窗口**时先唤到最前——只看「是否隐藏/最小化」会让"把别的程序切到前面"之后的第一次按键落进"收起"分支（窗口本就被盖住、用户看不到这次收起），于是必须连按两次才唤得出来；只有它已经是当前窗口时才最小化。两个例外：设置窗口打开时焦点必然不在主窗口（`_modalOpen`），按键收起主窗口更合理；置顶且可见时窗口本来就压在别人上面，按键直接收起。`GlobalHotkeyService` 使用 Win32 `RegisterHotKey`、`UnregisterHotKey` 和 WPF `HwndSource.AddHook` 接收 `WM_HOTKEY`。

配置替换顺序为：解析 -> 检查两个动作内部冲突 -> 尝试注册新组合 -> 新组合成功后注销旧组合 -> 更新内存设置 -> 原子保存。任一步失败都继续使用旧组合和旧持久化设置，并在设置窗口显示“格式无效”或“快捷键已被占用”。启动时单个快捷键注册失败不能阻止事项窗口启动。

弹出快捷键对最小化和边缘隐藏状态执行恢复并激活窗口；最小化快捷键执行 `WindowState=Minimized`。窗口是否始终置顶由设置项 `AlwaysOnTop` 决定（默认关闭），保存后立即作用于 `Window.Topmost` 并随状态持久化。

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
