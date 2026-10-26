using System.Windows;

namespace DynamicIsland.App;

/// <summary>
/// The expanded view of one widget (ADR 0022): created once, kept while the widget is shown, and updated in place with
/// each snapshot, so interactive controls (a slider being dragged, a focused button) survive updates. UI thread only.
/// </summary>
public abstract class WidgetView
{
    public abstract UIElement Element { get; }

    /// <summary>Shows <paramref name="snapshot"/> (the widget's own snapshot type).</summary>
    public abstract void Update(object snapshot);
}
