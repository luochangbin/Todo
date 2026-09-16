# AR-001 任务清单

## 1. 桌面备忘录实现

- [x] 1.1 创建 `TodoWidget.sln`、Core/Persistence/Desktop 三个项目和无外部依赖测试 runner，固定 `net10.0-windows` 与 Windows x64 发布配置（Requirement: 免安装发布；Scenario: 直接启动；Design: 技术边界与项目结构）
- [x] 1.2 实现事项模型、可替换时钟、分组排序、完成状态迁移、跨组拖动限制和 7 天历史过滤（Requirement: 事项分组与拖动排序；Scenario: 默认分组顺序；Design: 领域模型与不变量）
- [x] 1.3 实现本地 JSON 完整状态格式、临时文件写入、原子替换、串行保存和损坏/失败保护（Requirement: 本地持久化与历史过滤；Scenario: 持久化失败；Design: 持久化格式和一致性）
- [x] 1.4 实现无边框圆角主窗口、事项复选框/中划线、添加编辑、同组鼠标排序和显示历史开关（Requirement: 勾选完成；Scenario: 勾选未完成事项；Design: WPF 窗口与视觉交互）
- [x] 1.5 实现标题拖动、工作区边缘检测、同一窗口隐藏点收拢/悬停恢复、多显示器坐标校正与位置持久化（Requirement: 桌面窗口停靠与弹出；Scenario: 隐藏点弹出；Design: WPF 窗口与视觉交互）
- [x] 1.6 实现背景色设置、快捷键解析、Win32 全局热键注册/替换和错误提示（Requirement: 外观和快捷键设置；Scenario: 快捷键冲突或无效；Design: 设置与全局快捷键）
- [x] 1.7 接入应用启动恢复、关闭保存、错误提示和单实例主窗口生命周期（Requirement: 本地持久化与历史过滤；Scenario: 重启恢复；Design: 应用生命周期与错误处理）
- [x] 1.8 编写 Core/Persistence/快捷键回归测试，覆盖每个确定性业务不变量和故障边界（Requirement: 事项分组与拖动排序；Scenario: 跨分组拖动约束；Design: 测试策略）

## 2. 验证与交付

- [x] 2.1 使用用户级 .NET SDK 构建 Debug/Release，并确认构建命令和目标框架（Requirement: 免安装发布；Scenario: 直接启动；Design: Delivery Contract）
- [x] 2.2 运行无外部依赖回归测试 runner，确认排序、完成移动、历史过滤、持久化故障和热键解析场景通过（Requirement: 本地持久化与历史过滤；Scenario: 持久化失败；Design: 测试策略）
- [x] 2.3 生成 `publish/win-x64` 自包含单文件并真实启动主 exe，确认出现可交互唯一窗口（Requirement: 免安装发布；Scenario: 直接启动；Design: Delivery Contract）
- [x] 2.4 对发布应用执行核心桌面冒烟：添加/勾选/中划线/历史显示/设置入口/边缘隐藏点恢复，并记录可验证结果（Requirement: 桌面窗口停靠与弹出；Scenario: 边缘隐藏；Design: 测试策略）



