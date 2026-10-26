using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.App;

/// <summary>
/// The expanded view of one widget (ADR 0022): created once, kept while the widget is shown, and updated in place with
/// each snapshot, so interactive controls (a slider being dragged, a focused button) survive updates. UI thread only.
/// </summary>
public abstract class WidgetView
{
    public static readonly Brush Primary = Frozen(Color.FromRgb(0xF2, 0xF3, 0xF5));
    public static readonly Brush Secondary = Frozen(Color.FromArgb(0xB3, 0xF2, 0xF3, 0xF5));

    public abstract UIElement Element { get; }

    /// <summary>Shows <paramref name="snapshot"/> (the widget's own snapshot type).</summary>
    public abstract void Update(object snapshot);

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
