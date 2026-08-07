using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace RemoteNest.Views;

/// <summary>
/// ModernWpf's NumberBox exposes its editable area through an inner TextBox template
/// part that does not inherit the NumberBox's AutomationProperties.Name — screen
/// readers focus the inner box and announce nothing. This copies the name down once
/// the template is applied (Loaded fires when the hosting tab first shows the box).
/// </summary>
internal static class NumberBoxAccessibility
{
    public static void Attach(params ModernWpf.Controls.NumberBox[] boxes)
    {
        foreach (var box in boxes)
            box.Loaded += OnNumberBoxLoaded;
    }

    private static void OnNumberBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ModernWpf.Controls.NumberBox box) return;
        var name = AutomationProperties.GetName(box);
        if (string.IsNullOrEmpty(name)) return;

        var inner = FindDescendant<TextBox>(box);
        if (inner is not null)
            AutomationProperties.SetName(inner, name);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is T nested) return nested;
        }
        return null;
    }
}
