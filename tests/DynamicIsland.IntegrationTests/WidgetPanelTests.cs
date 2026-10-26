using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DynamicIsland.App;
using DynamicIsland.Core.Components;
using DynamicIsland.Core.Widgets;
using DynamicIsland.TestSupport;

namespace DynamicIsland.IntegrationTests;

/// <summary>The expanded panel's widget slots on a real <see cref="NotchView"/> (ADR 0018).</summary>
public sealed class WidgetPanelTests : IDisposable
{
    private readonly StaDispatcherThread _ui = new();

    public void Dispose() => _ui.Dispose();

    [Fact]
    public void Slots_show_in_order_failed_widgets_get_a_note_and_empty_ones_take_no_space()
    {
        var (texts, ids, placeholder) = _ui.Invoke(() =>
        {
            var view = new NotchView();
            view.ShowWidgets(
                [
                    Slot("media", "Media", ComponentState.Healthy, "Song"),
                    Slot("system", "System stats", ComponentState.Failed, null),
                    Slot("clock", "Clock and date", ComponentState.Starting, null),
                ],
                slot => slot.Snapshot is string text ? new TextBlock { Text = text } : null);
            var elements = view.WidgetElements;
            return (
                elements.OfType<TextBlock>().Select(t => t.Text).ToArray(),
                elements.Select(e => AutomationProperties.GetAutomationId(e)).ToArray(),
                ((FrameworkElement)view.FindName("WidgetPlaceholder")).Visibility);
        });

        Assert.Equal(["Song", "System stats isn't available right now."], texts);
        Assert.Equal(["Widget.media", "Widget.system"], ids);
        Assert.Equal(Visibility.Collapsed, placeholder);
    }

    [Fact]
    public void Without_content_the_hint_stays()
    {
        var placeholder = _ui.Invoke(() =>
        {
            var view = new NotchView();
            view.ShowWidgets([Slot("clock", "Clock and date", ComponentState.Healthy, null)], _ => null);
            return ((FrameworkElement)view.FindName("WidgetPlaceholder")).Visibility;
        });

        Assert.Equal(Visibility.Visible, placeholder);
    }

    private static WidgetSlot Slot(string id, string label, ComponentState state, object? snapshot) =>
        new(id, label, new ComponentStatus(WidgetRegistry.ComponentName(id), state), snapshot);
}
