using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TodoWidget.Core;
using TodoWidget.Desktop.Interop;
using Path = System.Windows.Shapes.Path;

namespace TodoWidget.Desktop;

public partial class MainWindow : Window
{
    private const double DragStartPixels = 4;
    private const double ResizeBand = 6;

    private readonly AppState _state;
    private readonly GlobalHotkeyService _hotkeys;
    private readonly DockManager _dock;
    private bool _finalSaveStarted;
    private bool _finalSaveDone;
    private Guid? _editingId;
    private TextBox? _activeEditor;
    private Guid? _dragItemId;
    private Point _dragOrigin;
    private bool _dragArmed;
    private Guid? _pendingDeleteId;
    private bool _draftActive;
    private TextBox? _draftBox;
    private bool _modalOpen;
    private Border? _dragIndicator;
    private bool _resizeActive;
    private bool _resizeLeft;
    private bool _resizeRight;
    private bool _resizeTop;
    private bool _resizeBottom;
    private Point _resizeCursorStart;
    private Rect _resizeStartBounds;

    public MainWindow(AppState state)
    {
        InitializeComponent();
        _state = state;

        _hotkeys = new GlobalHotkeyService(this);
        _hotkeys.RegisterHandler(OnToggleHotkey);

        _dock = new DockManager(this, OnPlacementChanged);
        MouseEnter += OnWindowMouseEnter;
        MouseLeave += OnWindowMouseLeave;
        Closing += MainWindow_Closing;

        _tray = new TrayIcon(OnToggleHotkey, ExitFromTray);
    }

    private TrayIcon? _tray;

    public void Initialize()
    {
        ApplyVisuals();
        var (works, primary) = GetScreenWorks();
        var corrected = DockCalculator.CorrectPlacement(_state.Settings.Window, works, primary);
        _state.Settings = _state.Settings with { Window = corrected };
        _dock.Initialize(corrected);

        Topmost = _state.Settings.AlwaysOnTop;
        PinButton.IsChecked = _state.Settings.AlwaysOnTop;

        RegisterStartupHotkeys();

        // 首次运行（含旧状态文件缺少标记）默认开启随系统启动；
        // 用户从托盘菜单退出时会取消，之后不再自动开启（也尊重用户在系统里关掉启动项）。
        if (!_state.Settings.AutoStartConfigured)
        {
            AutoStart.Enable();
            _state.Settings = _state.Settings with { AutoStartConfigured = true };
            Persist();
        }

        Render();

        if (!string.IsNullOrEmpty(_state.StartupError))
        {
            ShowError(_state.StartupError!);
        }
    }

    // ---------- 视觉 ----------

    // 窗口底默认是用户选色；Hover/Input/Separator 由该底色派生，保证任意底色都自洽。
    // 当用户选择「无背景」时面板填充与边框完全透明，派生色改用默认底色作基准。
    private void ApplyVisuals()
    {
        bool noBackground = AppSettings.IsNoBackground(_state.Settings.BackgroundColor);

        Color bg;
        if (noBackground)
        {
            // 无背景时用文字色亮度选明暗分支，派生基准取默认底色
            bg = ColorUtil.TryParse(_state.Settings.TextColor, out var textForBranch)
                 && ColorUtil.Luminance(textForBranch) >= 0.55
                ? Color.FromRgb(0xE9, 0xE4, 0xD8)
                : Color.FromRgb(0x1B, 0x1C, 0x20);
        }
        else if (!ColorUtil.TryParse(_state.Settings.BackgroundColor, out bg))
        {
            bg = ColorUtil.TryParse(AppSettings.Default.BackgroundColor, out var d)
                ? d
                : Color.FromRgb(0x1B, 0x1C, 0x20);
        }

        // 只有主窗口面板（SurfaceBrush）半透明；ChromeBrush 保持不透明，供设置窗口
        // 与删除确认气泡使用，保证调设置、确认删除时文字清晰。
        // Surface* 前景色的 alpha 跟随面板但保留可读下限，避免文字"挡住"桌面其它内容。
        double opacity = _state.Settings.Opacity;
        byte alpha = ColorUtil.AlphaFromOpacity(opacity);
        byte textAlpha = ColorUtil.TextAlphaFromOpacity(opacity);
        bool light = ColorUtil.Luminance(bg) >= 0.55;
        var res = Application.Current.Resources;
        res["Todo.ChromeBrush"] = new SolidColorBrush(bg);
        // 「无背景」用 alpha=1 的近乎透明填充：alpha=0 时 Windows 分层窗口会让鼠标事件
        // 整窗穿透，导致拖不动、缩放不了、悬停也失效；1/255 肉眼等价于全透明但保留命中测试。
        res["Todo.SurfaceBrush"] = noBackground
            ? new SolidColorBrush(ColorUtil.WithAlpha(bg, 1))
            : new SolidColorBrush(ColorUtil.WithAlpha(bg, alpha));

        // 主题前景色：由面板底色的明暗分支决定。设置窗口、删除确认气泡、标题栏与各类图标
        // 都用它，因此不受用户「待办文字颜色」影响。
        Color themeFg, themeMuted;
        if (light)
        {
            themeFg = Color.FromRgb(0x1A, 0x1C, 0x20);
            themeMuted = Color.FromRgb(0x5F, 0x66, 0x72);
        }
        else
        {
            themeFg = Color.FromRgb(0xE9, 0xEA, 0xEE);
            themeMuted = Color.FromRgb(0x8C, 0x91, 0x9B);
        }

        // 待办文字色：用户配置，只作用于首页的事项文本；弱化色（已完成项）由它派生
        if (!ColorUtil.TryParse(_state.Settings.TextColor, out var textFg))
        {
            textFg = ColorUtil.TryParse(AppSettings.Default.TextColor, out var fallbackFg)
                ? fallbackFg
                : Color.FromRgb(0xE9, 0xEA, 0xEE);
        }
        Color textMuted = ColorUtil.Mix(textFg, bg, 0.42);

        Color accent, accentHover;
        if (light)
        {
            accent = Color.FromRgb(0x4F, 0x6B, 0xED);
            accentHover = Color.FromRgb(0x3F, 0x59, 0xD6);
            res["Todo.SeparatorBrush"] = new SolidColorBrush(ColorUtil.WithAlpha(ColorUtil.Derive(bg, 0.09, towardWhite: false), alpha));
            res["Todo.HoverBrush"] = new SolidColorBrush(ColorUtil.WithAlpha(ColorUtil.Derive(bg, 0.05, towardWhite: false), alpha));
            res["Todo.InputBrush"] = new SolidColorBrush(ColorUtil.WithAlpha(Colors.White, alpha));
        }
        else
        {
            accent = Color.FromRgb(0x5B, 0x7C, 0xFA);
            accentHover = Color.FromRgb(0x6E, 0x8B, 0xFF);
            res["Todo.SeparatorBrush"] = new SolidColorBrush(ColorUtil.WithAlpha(ColorUtil.Derive(bg, 0.07, towardWhite: true), alpha));
            res["Todo.HoverBrush"] = new SolidColorBrush(ColorUtil.WithAlpha(ColorUtil.Derive(bg, 0.07, towardWhite: true), alpha));
            res["Todo.InputBrush"] = new SolidColorBrush(ColorUtil.WithAlpha(ColorUtil.Derive(bg, 0.25, towardWhite: false), alpha));
        }

        // 主窗口面板边框：无背景时同样用 alpha=1 保持可命中的边缘（用于拖动缩放）
        var separator = (SolidColorBrush)res["Todo.SeparatorBrush"];
        res["Todo.PanelBorderBrush"] = noBackground
            ? new SolidColorBrush(ColorUtil.WithAlpha(separator.Color, 1))
            : new SolidColorBrush(separator.Color);

        // 不透明版本：设置窗口与确认气泡用
        res["Todo.FgBrush"] = new SolidColorBrush(themeFg);
        res["Todo.MutedBrush"] = new SolidColorBrush(themeMuted);
        res["Todo.AccentBrush"] = new SolidColorBrush(accent);
        res["Todo.AccentHoverBrush"] = new SolidColorBrush(accentHover);
        // 面板前景版本：主窗口的标题栏与图标用
        res["Todo.SurfaceFgBrush"] = new SolidColorBrush(ColorUtil.WithAlpha(themeFg, textAlpha));
        res["Todo.SurfaceMutedBrush"] = new SolidColorBrush(ColorUtil.WithAlpha(themeMuted, textAlpha));
        res["Todo.SurfaceAccentBrush"] = new SolidColorBrush(ColorUtil.WithAlpha(accent, textAlpha));
        // 待办文本专用：只有首页的事项文本（含编辑框/草稿行）用这两个
        res["Todo.TextFgBrush"] = new SolidColorBrush(ColorUtil.WithAlpha(textFg, textAlpha));
        res["Todo.TextMutedBrush"] = new SolidColorBrush(ColorUtil.WithAlpha(textMuted, textAlpha));
    }

    /// <summary>设置窗口改动外观（背景色/字体色/不透明度）时的即时预览（不落盘）。</summary>
    public void PreviewAppearance(string backgroundColor, string textColor, double opacity)
    {
        _state.Settings = _state.Settings with
        {
            BackgroundColor = backgroundColor,
            TextColor = textColor,
            Opacity = AppSettings.ClampOpacity(opacity),
        };
        ApplyVisuals();
    }

    private void Render()
    {
        DeleteConfirm.IsOpen = false;
        var visible = _state.Todos.GetVisible(_state.Settings.CompletedRange);
        int completed = visible.Count(i => i.IsCompleted);
        CounterText.Text = $"已完成 {completed}/{visible.Count}";

        _activeEditor = null;
        _draftBox = null;
        RowsPanel.Children.Clear();
        _rowHosts.Clear();

        // 草稿行插在「普通区开头」，也就是新事项提交后所在的位置
        int draftSlot = visible.Count(i => i.IsPinned && !i.IsCompleted);
        int position = 0;
        var focusBox = (TextBox?)null;
        foreach (var item in visible)
        {
            if (_draftActive && position == draftSlot)
            {
                var draftRow = BuildDraftRow();
                RowsPanel.Children.Add(draftRow);
                focusBox = _draftBox;
            }
            RowsPanel.Children.Add(BuildRow(item));
            position++;
        }
        if (_draftActive && focusBox is null)
        {
            RowsPanel.Children.Add(BuildDraftRow());
            focusBox = _draftBox;
        }

        // 布局完成后再聚焦，元素此时才进入可视树
        if (focusBox is not null)
        {
            var box = focusBox;
            Dispatcher.BeginInvoke(new Action(() => { if (_draftActive) box.Focus(); }), DispatcherPriority.Input);
        }

        if (_dock.IsCollapsed)
        {
            // 隐藏点状态不渲染任何列表内容尺寸，避免挤压
        }
    }

    private readonly List<FrameworkElement> _rowHosts = new();

    private FrameworkElement BuildRow(TodoItem item)
    {
        var host = new Border
        {
            Tag = item.Id,
            MinHeight = 34,
            Margin = new Thickness(6, 1, 6, 1),
            Style = (Style)Application.Current.Resources["Todo.Row"],
        };
        AutomationProperties.SetAutomationId(host, $"Row-{item.Id:N}");

        var row = new Grid { MinHeight = 34 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.Child = row;

        var check = new CheckBox
        {
            IsChecked = item.IsCompleted,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(check, $"Check-{item.Id:N}");
        AutomationProperties.SetName(check, item.Text);
        check.Checked += (_, _) => OnCheckChanged(item.Id, true);
        check.Unchecked += (_, _) => OnCheckChanged(item.Id, false);
        Grid.SetColumn(check, 0);
        row.Children.Add(check);

        if (_editingId == item.Id)
        {
            var editor = new TextBox
            {
                Text = item.Text,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                MinHeight = 26,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3, 6, 3),
            };
            AutomationProperties.SetAutomationId(editor, "RowEditInput");
            editor.SetResourceReference(Control.ForegroundProperty, "Todo.TextFgBrush");
            Grid.SetColumn(editor, 1);
            row.Children.Add(editor);
            _activeEditor = editor;

            var editingId = item.Id;
            bool cancelled = false;
            // Enter 提交；Shift+Enter 换行（AcceptsReturn 已开启，不拦截即插入换行）
            editor.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) return;
                    CommitEdit(editingId, editor.Text);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    cancelled = true;
                    _editingId = null;
                    Render();
                    e.Handled = true;
                }
            };
            editor.LostKeyboardFocus += (_, _) =>
            {
                if (cancelled) return;
                if (_editingId == editingId)
                {
                    CommitEdit(editingId, editor.Text);
                }
            };

            _rowHosts.Add(host);
            return host;
        }

        var text = new TextBlock
        {
            Text = item.Text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Margin = new Thickness(0, 1, 6, 1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        // 用资源引用而不是固定 brush 实例，外观即时预览才能作用到已存在的行
        text.SetResourceReference(TextBlock.ForegroundProperty, "Todo.TextFgBrush");
        AutomationProperties.SetAutomationId(text, $"Text-{item.Id:N}");
        AutomationProperties.SetName(text, item.Text);
        AutomationProperties.SetHelpText(text, item.IsCompleted ? "已完成" : "进行中");
        if (item.IsCompleted)
        {
            text.SetResourceReference(TextBlock.ForegroundProperty, "Todo.TextMutedBrush");
            text.TextDecorations = TextDecorations.Strikethrough;
        }
        text.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                BeginEdit(item.Id);
            }
            else
            {
                ArmRowDrag(host, item.Id, e);
            }
        };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        // 置顶项的常驻标记：在操作区之前加入，悬停展开操作区时被其底色盖住
        if (item.IsPinned && !item.IsCompleted)
        {
            var mark = new Path
            {
                Data = (Geometry)Application.Current.Resources["Todo.PinIcon"],
                Style = (Style)Application.Current.Resources["Todo.PinMark"],
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Visibility = Visibility.Visible,
            };
            AutomationProperties.SetAutomationId(mark, $"Pin-{item.Id:N}");
            AutomationProperties.SetName(mark, "已置顶");
            Grid.SetColumn(mark, 1);
            row.Children.Add(mark);
        }

        var actions = BuildRowActions(item);
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);

        host.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (IsNonDragSource(e.OriginalSource))
            {
                _dragArmed = false;
                return;
            }
            if (_editingId is not null) return;
            ArmRowDrag(host, item.Id, e);
        };
        host.PreviewMouseMove += (_, e) =>
        {
            if (_dragArmed && _dragItemId == item.Id && e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(this);
                if (Math.Abs(pos.X - _dragOrigin.X) > DragStartPixels
                    || Math.Abs(pos.Y - _dragOrigin.Y) > DragStartPixels)
                {
                    _dragArmed = false;
                    var data = new DataObject("TodoWidget.ItemId", item.Id);
                    DragDrop.DoDragDrop(host, data, DragDropEffects.Move);
                    Render();
                }
            }
        };

        _rowHosts.Add(host);
        return host;
    }

    private static bool IsNonDragSource(object original)
    {
        var d = original as DependencyObject;
        while (d is not null)
        {
            if (d is CheckBox or TextBox or Button) return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    private void ArmRowDrag(FrameworkElement row, Guid id, MouseButtonEventArgs e)
    {
        if (_dock.IsCollapsed || _editingId is not null) return;
        _dragItemId = id;
        _dragArmed = true;
        _dragOrigin = e.GetPosition(this);
    }

    // ---------- 行内操作 ----------

    private Border BuildRowActions(TodoItem item)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        var edit = BuildRowIcon("Todo.EditIcon", "编辑");
        edit.Click += (_, _) => BeginEdit(item.Id);
        panel.Children.Add(edit);

        if (!item.IsCompleted)
        {
            var pin = BuildRowIcon("Todo.PinIcon", item.IsPinned ? "取消置顶" : "置顶");
            if (item.IsPinned)
            {
                pin.SetResourceReference(Control.ForegroundProperty, "Todo.SurfaceAccentBrush");
            }
            pin.Click += (_, _) => TogglePin(item.Id);
            panel.Children.Add(pin);
        }

        var delete = BuildRowIcon("Todo.DeleteIcon", "删除");
        delete.Click += (_, _) => OpenDeleteConfirm(item, delete);
        panel.Children.Add(delete);

        return new Border
        {
            Style = (Style)Application.Current.Resources["Todo.RowActions"],
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Child = panel,
        };
    }

    private void TogglePin(Guid id)
    {
        var item = _state.Todos.Items.FirstOrDefault(i => i.Id == id);
        if (item is null || item.IsCompleted) return;

        if (item.IsPinned)
        {
            _state.Todos.Unpin(id);
        }
        else
        {
            _state.Todos.Pin(id);
        }
        Render();
        Persist();
    }

    private static Button BuildRowIcon(string geometryKey, string accessibleName)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["Todo.RowIcon"],
            ToolTip = accessibleName,
            Content = new Path
            {
                Data = (Geometry)Application.Current.Resources[geometryKey],
                Style = (Style)Application.Current.Resources["Todo.RowIconGlyph"],
            },
        };
        AutomationProperties.SetName(button, accessibleName);
        return button;
    }

    // ---------- 删除确认 ----------

    private void OpenDeleteConfirm(TodoItem item, Button anchor)
    {
        _pendingDeleteId = item.Id;
        DeleteConfirmText.Text = item.Text;
        DeleteConfirm.PlacementTarget = anchor;
        DeleteConfirm.IsOpen = true;
    }

    private void DeleteCancelButton_Click(object sender, RoutedEventArgs e) => DeleteConfirm.IsOpen = false;

    private void DeleteConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var id = _pendingDeleteId;
        DeleteConfirm.IsOpen = false;
        if (id is not { } target) return;

        _state.Todos.Delete(target);
        Render();
        Persist();
    }

    private void DeleteConfirm_Closed(object? sender, EventArgs e) => _pendingDeleteId = null;

    // ---------- 事项操作 ----------

    private void OnCheckChanged(Guid id, bool completed)
    {
        _state.Todos.SetCompleted(id, completed);
        Render();
        Persist();
    }

    private void BeginEdit(Guid id)
    {
        _editingId = id;
        Render();
        if (_activeEditor is not null)
        {
            _activeEditor.Focus();
            _activeEditor.SelectAll();
        }
    }

    private void CommitEdit(Guid id, string newText)
    {
        if (string.IsNullOrWhiteSpace(newText))
        {
            _editingId = null;
            Render();
            return;
        }
        var item = _state.Todos.Items.FirstOrDefault(i => i.Id == id);
        if (item is not null && item.Text != newText)
        {
            _state.Todos.UpdateText(id, newText);
        }
        _editingId = null;
        Render();
        Persist();
    }

    // ---------- 新增事项（标题栏 ➕ → 列表内联草稿行） ----------

    private void BeginDraft()
    {
        if (_draftActive) return;
        _draftActive = true;
        Render();
    }

    private void CommitDraft()
    {
        string text = _draftBox?.Text.Trim() ?? string.Empty;
        if (!_draftActive) return;
        _draftActive = false;
        if (text.Length == 0)
        {
            Render();
            return;
        }

        _state.Todos.Create(text);
        Render();
        Persist();
    }

    private void CancelDraft()
    {
        if (!_draftActive) return;
        _draftActive = false;
        Render();
    }

    private FrameworkElement BuildDraftRow()
    {
        var host = new Border
        {
            MinHeight = 34,
            Margin = new Thickness(6, 1, 6, 1),
        };
        AutomationProperties.SetAutomationId(host, "DraftRow");

        var row = new Grid { MinHeight = 34 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.Child = row;

        var box = new TextBox
        {
            MinHeight = 26,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 3, 6, 3),
        };
        box.SetResourceReference(Control.ForegroundProperty, "Todo.TextFgBrush");
        AutomationProperties.SetAutomationId(box, "DraftInput");
        box.PreviewKeyDown += DraftBox_PreviewKeyDown;
        box.LostKeyboardFocus += (_, _) => { if (_draftActive) CommitDraft(); };
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        _draftBox = box;
        return host;
    }

    private void Persist()
    {
        _ = PersistAsync();
    }

    private async Task PersistAsync()
    {
        var error = await _state.SaveAsync();
        if (error is not null)
        {
            ShowError(error);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBar.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorBar.Visibility = Visibility.Collapsed;
    }

    // ---------- 拖放排序 ----------

    private void RowsPanel_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("TodoWidget.ItemId"))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        var slot = SlotAt(e.GetPosition(RowsPanel));
        ShowInsertIndicator(slot);
    }

    private void RowsPanel_DragLeave(object sender, DragEventArgs e)
    {
        HideInsertIndicator();
    }

    private void RowsPanel_Drop(object sender, DragEventArgs e)
    {
        HideInsertIndicator();
        if (e.Data.GetData("TodoWidget.ItemId") is not Guid id) return;
        int slot = SlotAt(e.GetPosition(RowsPanel));
        ApplyDrop(id, slot);
        e.Handled = true;
    }

    private int SlotAt(Point p)
    {
        double y = p.Y;
        int slot = 0;
        foreach (var host in _rowHosts)
        {
            var pos = host.TranslatePoint(new Point(0, host.ActualHeight / 2), RowsPanel);
            if (y > pos.Y) slot++;
            else break;
        }
        return slot;
    }

    private void ApplyDrop(Guid draggedId, int slot)
    {
        var full = _state.Todos.Items;
        if (full.Count == 0) return;
        if (full.All(i => i.Id == draggedId)) return;

        var beforeIds = _rowHosts.Select(h => (Guid)h.Tag!).ToList();
        int targetIndex;
        if (beforeIds.Count == 0)
        {
            targetIndex = 0;
        }
        else if (slot <= 0)
        {
            targetIndex = FullIndex(full, beforeIds[0]);
        }
        else if (slot >= beforeIds.Count)
        {
            targetIndex = FullIndex(full, beforeIds[^1]) + 1;
        }
        else
        {
            targetIndex = FullIndex(full, beforeIds[slot]);
        }

        targetIndex = Math.Clamp(targetIndex, 0, full.Count);
        _state.Todos.Move(draggedId, targetIndex);
        Render();
        Persist();
    }

    private static int FullIndex(IReadOnlyList<TodoItem> items, Guid id)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Id == id) return i;
        }
        return 0;
    }

    private void ShowInsertIndicator(int slot)
    {
        HideInsertIndicator();
        var indicator = new Border
        {
            Height = 2,
            Margin = new Thickness(6, 0, 6, 0),
            CornerRadius = new CornerRadius(0),
            IsHitTestVisible = false,
        };
        indicator.SetResourceReference(Border.BackgroundProperty, "Todo.SurfaceAccentBrush");
        int index = Math.Clamp(slot, 0, RowsPanel.Children.Count);
        RowsPanel.Children.Insert(index, indicator);
        _dragIndicator = indicator;
    }

    private void HideInsertIndicator()
    {
        if (_dragIndicator is null) return;
        RowsPanel.Children.Remove(_dragIndicator);
        _dragIndicator = null;
    }

    // ---------- 停靠 ----------

    private void OnWindowMouseEnter(object sender, MouseEventArgs e)
    {
        if (_dock.IsCollapsed)
        {
            _dock.RestoreFromHidden();
        }
    }

    // 因边缘触发而滑出显示后，鼠标离开就自动收回（QQ 式）；停靠状态保留。
    private void OnWindowMouseLeave(object sender, MouseEventArgs e)
    {
        if (Mouse.LeftButton == MouseButtonState.Pressed) return; // 拖动/缩放/排序进行中
        if (_modalOpen || DeleteConfirm.IsOpen) return;
        _dock.ScheduleCollapse();
    }

    private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_dock.IsCollapsed) return;
        if (e.OriginalSource is DependencyObject d && HasAncestor<ButtonBase>(d))
        {
            return;
        }
        try
        {
            DragMove();
            _dock.OnTitleDragEnded();
        }
        catch (InvalidOperationException)
        {
            // 用户未真正按住左键，忽略
        }
    }

    private static bool HasAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T) return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    private void OnPlacementChanged(WindowPlacement placement)
    {
        _state.Settings = _state.Settings with { Window = placement };
        Persist();
    }

    private void RestoreHiddenOrNormal()
    {
        _dock.RestoreFromHidden();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
    }

    // 单一快捷键：隐藏/最小化时唤出并激活，正常显示时最小化。
    private void OnToggleHotkey()
    {
        if (_dock.IsCollapsed || WindowState == WindowState.Minimized)
        {
            RestoreHiddenOrNormal();
            Show();
            Activate();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            return;
        }
        WindowState = WindowState.Minimized;
    }

    // ---------- 快捷键注册 ----------

    private void RegisterStartupHotkeys()
    {
        var toggle = ParseHotkey(_state.Settings.ToggleHotkey);
        if (toggle is not null && !_hotkeys.TryApply(toggle, out var error) && error is not null)
        {
            _state.StartupError = error;
        }
    }

    private static ParsedHotkey? ParseHotkey(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null
            : HotkeyParser.TryParse(text, out var parsed, out _) ? parsed : null;

    public AppSettings CurrentSettings => _state.Settings;

    public WindowPlacement CurrentWindowPlacement => _dock.CurrentPlacement;

    public string? TryApplySettings(AppSettings candidate)
    {
        var validation = AppHotkeyRules.ValidateToggleHotkey(candidate.ToggleHotkey);
        if (validation is not null) return validation;
        if (!AppSettings.IsNoBackground(candidate.BackgroundColor)
            && !ColorUtil.TryParse(candidate.BackgroundColor, out _))
        {
            return "背景颜色格式无效，请输入 #RRGGBB 或 none";
        }
        if (!ColorUtil.TryParse(candidate.TextColor, out _))
        {
            return "字体颜色格式无效，请输入 #RRGGBB 格式";
        }

        var oldToggle = ParseHotkey(_state.Settings.ToggleHotkey);
        var newToggle = ParseHotkey(candidate.ToggleHotkey);
        if (newToggle != oldToggle
            && !_hotkeys.TryApply(newToggle, out var hotkeyError)
            && hotkeyError is not null)
        {
            return hotkeyError;
        }

        _state.Settings = candidate;
        Topmost = candidate.AlwaysOnTop;
        PinButton.IsChecked = candidate.AlwaysOnTop;
        ApplyVisuals();
        Render();
        Persist();
        return null;
    }

    // ---------- 屏幕几何 ----------

    private (IReadOnlyList<RectD> Works, RectD Primary) GetScreenWorks()
    {
        double scale = DpiHelper.GetWindowDpiScale(this);
        if (scale <= 0) scale = 1.0;
        var works = new List<RectD>();
        RectD? primary = null;
        foreach (var monitor in NativeMethods.GetAllMonitors())
        {
            var rect = new RectD(
                monitor.Work.Left / scale, monitor.Work.Top / scale,
                (monitor.Work.Right - monitor.Work.Left) / scale,
                (monitor.Work.Bottom - monitor.Work.Top) / scale);
            works.Add(rect);
            if (monitor.IsPrimary) primary = rect;
        }
        if (primary is null && works.Count > 0) primary = works[0];
        primary ??= new RectD(0, 0, 1280, 800);
        return (works, primary);
    }

    // ---------- 事件 ----------

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(this, _state.Settings)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        var (x, y) = FindDialogPosition(dialog.Width, dialog.Height);
        dialog.Left = x;
        dialog.Top = y;
        // 模态对话框期间鼠标会离开主窗口，此时不能触发停靠收回
        _modalOpen = true;
        try
        {
            dialog.ShowDialog();
        }
        finally
        {
            _modalOpen = false;
        }
    }

    // 设置窗口默认贴在主窗口右侧（放不下改左侧），避免它完全盖住主窗口、
    // 导致拖动不透明度滑块时的实时预览看不见。
    private (double X, double Y) FindDialogPosition(double dialogWidth, double dialogHeight)
    {
        var (works, primary) = GetScreenWorks();
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        double centerX = Left + w / 2;
        double centerY = Top + h / 2;

        var work = works.FirstOrDefault(r =>
            centerX >= r.Left && centerX <= r.Right && centerY >= r.Top && centerY <= r.Bottom);
        if (work is null || work.Width <= 0) work = primary;

        double right = Left + w + Gap;
        double left = Left - dialogWidth - Gap;
        double x = right + dialogWidth <= work.Right ? right
            : left >= work.Left ? left
            : Math.Clamp(right, work.Left, Math.Max(work.Left, work.Right - dialogWidth));
        double y = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - dialogHeight));
        return (x, y);
    }

    private const double Gap = 12;

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        bool on = PinButton.IsChecked == true;
        _state.Settings = _state.Settings with { AlwaysOnTop = on };
        Topmost = on;
        Persist();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e) => BeginDraft();

    // Enter 提交；Shift+Enter 换行；Esc 取消
    private void DraftBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) return;
            CommitDraft();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelDraft();
            e.Handled = true;
        }
    }

    private void ErrorCloseButton_Click(object sender, RoutedEventArgs e) => HideError();

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DeleteConfirm.IsOpen)
        {
            DeleteConfirm.IsOpen = false;
            e.Handled = true;
        }
    }

    // 鼠标抬起即解除拖动武装：避免双击进入编辑后 _dragArmed 残留、随后误触发拖动
    private void MainWindow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragArmed = false;
        if (!_resizeActive) return;

        _resizeActive = false;
        _resizeLeft = _resizeRight = _resizeTop = _resizeBottom = false;
        ReleaseMouseCapture();
        Cursor = null;
        _dock.UpdateSizeFromWindow();
        e.Handled = true;
    }

    // ---------- 拖动改大小（无边框 + 透明窗口下系统缩放边框不可靠，自己实现） ----------

    private void MainWindow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_dock.IsCollapsed || _resizeActive || _editingId is not null || _draftActive) return;

        var (left, right, top, bottom) = HitResizeBands(e.GetPosition(this));
        if (!left && !right && !top && !bottom) return;

        _resizeLeft = left;
        _resizeRight = right;
        _resizeTop = top;
        _resizeBottom = bottom;
        _resizeActive = true;
        _resizeCursorStart = PointToScreenDip(e.GetPosition(this));
        _resizeStartBounds = new Rect(Left, Top, ActualWidth, ActualHeight);
        CaptureMouse();
        e.Handled = true;
    }

    private void MainWindow_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_resizeActive)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            var current = PointToScreenDip(e.GetPosition(this));
            double dx = current.X - _resizeCursorStart.X;
            double dy = current.Y - _resizeCursorStart.Y;
            var b = _resizeStartBounds;

            double left = b.Left;
            double top = b.Top;
            double width = b.Width;
            double height = b.Height;
            if (_resizeLeft) { left = b.Left + dx; width = b.Width - dx; }
            if (_resizeRight) { width = b.Width + dx; }
            if (_resizeTop) { top = b.Top + dy; height = b.Height - dy; }
            if (_resizeBottom) { height = b.Height + dy; }

            if (width < MinWidth) { if (_resizeLeft) left = b.Right - MinWidth; width = MinWidth; }
            if (height < MinHeight) { if (_resizeTop) top = b.Bottom - MinHeight; height = MinHeight; }

            Left = left;
            Top = top;
            Width = width;
            Height = height;
            e.Handled = true;
            return;
        }

        if (!_dock.IsCollapsed && e.LeftButton == MouseButtonState.Released)
        {
            UpdateResizeCursor(e.GetPosition(this));
        }
    }

    private (bool Left, bool Right, bool Top, bool Bottom) HitResizeBands(Point position)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        return (
            position.X <= ResizeBand,
            position.X >= w - ResizeBand,
            position.Y <= ResizeBand,
            position.Y >= h - ResizeBand);
    }

    private void UpdateResizeCursor(Point position)
    {
        var (left, right, top, bottom) = HitResizeBands(position);
        Cursor = (left, right, top, bottom) switch
        {
            (true, _, true, _) => Cursors.SizeNWSE,
            (_, true, _, true) => Cursors.SizeNWSE,
            (_, true, true, _) => Cursors.SizeNESW,
            (true, _, _, true) => Cursors.SizeNESW,
            (true, _, _, _) => Cursors.SizeWE,
            (_, true, _, _) => Cursors.SizeWE,
            (_, _, true, _) => Cursors.SizeNS,
            (_, _, _, true) => Cursors.SizeNS,
            _ => null,
        };
    }

    private Point PointToScreenDip(Point clientPoint)
    {
        var screen = PointToScreen(clientPoint);
        double scale = DpiHelper.GetWindowDpiScale(this);
        if (scale <= 0) scale = 1.0;
        return new Point(screen.X / scale, screen.Y / scale);
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_finalSaveDone) return;
        e.Cancel = true;
        if (_finalSaveStarted) return;
        _finalSaveStarted = true;
        _ = FinalCloseAsync();
    }

    // 从托盘菜单退出：取消随系统启动，再走正常的收尾保存流程
    private void ExitFromTray()
    {
        AutoStart.Disable();
        Close();
    }

    private async Task FinalCloseAsync()
    {
        _hotkeys.Dispose();
        _dock.Dispose();
        _tray?.Dispose();
        await PersistAsync();
        _finalSaveDone = true;
        Close();
    }
}
