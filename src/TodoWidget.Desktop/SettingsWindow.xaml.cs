using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TodoWidget.Core;

namespace TodoWidget.Desktop;

public partial class SettingsWindow : Window
{
    private readonly MainWindow _owner;
    private readonly AppSettings _original;
    private bool _saved;
    private bool _ready;

    public SettingsWindow(MainWindow owner, AppSettings current)
    {
        InitializeComponent();
        _owner = owner;
        _original = current;

        ColorInput.Text = current.BackgroundColor;
        TextColorInput.Text = current.TextColor;
        ToggleHotkeyInput.Text = current.ToggleHotkey;

        ApplyRangeSelection(current.CompletedRange);
        OpacityValue.Text = $"{(int)Math.Round(AppSettings.ClampOpacity(current.Opacity) * 100)}%";
        _ready = true;
        OpacitySlider.Value = Math.Round(AppSettings.ClampOpacity(current.Opacity) * 100.0);
        ApplyPresetColors();
        UpdatePresetSelection();
        // 颜色改动即时预览（与不透明度一致）
        ColorInput.TextChanged += (_, _) => OnAppearanceChanged();
        TextColorInput.TextChanged += (_, _) => OnAppearanceChanged();
        ToggleHotkeyInput.PreviewKeyDown += HotkeyField_PreviewKeyDown;

        // 非正常关闭（Alt+F4 等）也要回滚外观预览
        Closed += (_, _) => { if (!_saved) RevertPreview(); };
    }

    private void OnAppearanceChanged()
    {
        if (!_ready) return;
        UpdatePresetSelection();
        PreviewFromInputs();
    }

    // 颜色非法时退回当前生效值，避免预览把窗口闪成默认外观
    private void PreviewFromInputs()
    {
        if (!_ready) return;
        string background = ColorUtil.TryParse(ColorInput.Text, out _)
            ? ColorInput.Text.Trim()
            : _original.BackgroundColor;
        string textColor = ColorUtil.TryParse(TextColorInput.Text, out _)
            ? TextColorInput.Text.Trim()
            : _original.TextColor;
        _owner.PreviewAppearance(background, textColor, OpacitySlider.Value / 100.0);
    }

    private void RevertPreview() =>
        _owner.PreviewAppearance(_original.BackgroundColor, _original.TextColor, _original.Opacity);

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // 载入 XAML 时 Minimum/Maximum 会强制 Value 并触发本事件，此时 _owner 尚未赋值
        if (!_ready) return;
        OpacityValue.Text = $"{(int)Math.Round(e.NewValue)}%";
        PreviewFromInputs();
    }

    private void ApplyRangeSelection(CompletedRange range)
    {
        RangeWeek.IsChecked = range == CompletedRange.Week;
        RangeMonth.IsChecked = range == CompletedRange.Month;
        RangeAll.IsChecked = range == CompletedRange.All;
    }

    private CompletedRange ReadRangeSelection()
    {
        if (RangeMonth.IsChecked == true) return CompletedRange.Month;
        if (RangeAll.IsChecked == true) return CompletedRange.All;
        return CompletedRange.Week;
    }

    private Button[] BackgroundPresets => new[] { ColorPresetNone, ColorPreset1, ColorPreset2, ColorPreset3, ColorPreset4, ColorPreset5 };

    private Button[] TextPresets => new[] { TextPreset1, TextPreset2, TextPreset3, TextPreset4, TextPreset5, TextPreset6 };

    private void UpdatePresetSelection()
    {
        var selected = (Brush)Application.Current.Resources["Todo.AccentBrush"];
        var normal = (Brush)Application.Current.Resources["Todo.SeparatorBrush"];
        ApplySelection(BackgroundPresets, ColorInput.Text, selected, normal);
        ApplySelection(TextPresets, TextColorInput.Text, selected, normal);
    }

    private static void ApplySelection(IEnumerable<Button> buttons, string current, Brush selected, Brush normal)
    {
        string value = current.Trim();
        foreach (var button in buttons)
        {
            bool isCurrent = button.Tag is string hex
                && string.Equals(hex, value, StringComparison.OrdinalIgnoreCase);
            button.BorderBrush = isCurrent ? selected : normal;
            button.BorderThickness = new Thickness(isCurrent ? 2 : 1);
        }
    }

    private void ApplyPresetColors()
    {
        foreach (var button in BackgroundPresets.Concat(TextPresets))
        {
            if (button.Tag is string hex && ColorUtil.TryParse(hex, out var color))
            {
                button.Background = new SolidColorBrush(color);
            }
        }
    }
    private void ColorPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            ColorInput.Text = hex;
        }
    }

    private void TextPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            TextColorInput.Text = hex;
        }
    }

    private void HotkeyField_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
            or Key.System or Key.None)
        {
            return;
        }
        if (sender is not TextBox box) return;

        var mods = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods.Add("Alt");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods.Add("Win");

        string keyName = e.Key switch
        {
            Key.Space => "Space",
            Key.Enter => "Enter",
            Key.Tab => "Tab",
            Key.Back => "Backspace",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            >= Key.F1 and <= Key.F12 => "F" + (e.Key - Key.F1 + 1),
            >= Key.D0 and <= Key.D9 => (e.Key - Key.D0).ToString(),
            >= Key.A and <= Key.Z => e.Key.ToString(),
            _ => string.Empty,
        };

        if (keyName.Length == 0) return;
        box.Text = string.Join("+", mods.Concat(new[] { keyName }));
        box.CaretIndex = box.Text.Length;
        e.Handled = true;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var candidate = new AppSettings(
            ColorInput.Text.Trim(),
            TextColorInput.Text.Trim(),
            AppSettings.ClampOpacity(OpacitySlider.Value / 100.0),
            _owner.CurrentSettings.AlwaysOnTop,
            ToggleHotkeyInput.Text.Trim(),
            ReadRangeSelection(),
            _owner.CurrentWindowPlacement);

        var error = _owner.TryApplySettings(candidate);
        if (error is null)
        {
            _saved = true;
            Close();
        }
        else
        {
            // 保存失败（例如快捷键冲突）：回滚外观预览，保留窗口让用户修正
            RevertPreview();
            ErrorLabel.Text = error;
            ErrorLabel.Visibility = Visibility.Visible;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        RevertPreview();
        Close();
    }

    private void SettingsTitle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch (InvalidOperationException) { }
    }
}