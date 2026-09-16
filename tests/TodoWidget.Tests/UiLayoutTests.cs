using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TodoWidget.Desktop;

namespace TodoWidget.Tests;

public static class UiLayoutTests
{
    public static TestSuite Suite() => new("UI layout", new (string, Action)[]
    {
        ("history checkbox renders its label", HistoryCheckboxRendersItsLabel),
    });

    private static void HistoryCheckboxRendersItsLabel()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();

                var checkbox = new CheckBox { Content = "显示历史" };
                checkbox.ApplyTemplate();
                checkbox.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                var presenter = FindVisualChild<ContentPresenter>(checkbox);
                Test.True(presenter is not null, "checkbox template must render Content");
                Test.Eq(presenter!.Content?.ToString(), "显示历史");

                app.Shutdown();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null) throw error;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null) return descendant;
        }

        return null;
    }
}
