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
    public void One_tab_per_widget_with_content_in_order_and_only_the_selected_widget_is_shown()
    {
        var result = _ui.Invoke(() =>
        {
            var view = new NotchView();
            var changes = 0;
            view.SelectedWidgetChanged += (_, _) => changes++;
            WidgetSlot[] slots =
            [
                Slot("media", "Media", ComponentState.Healthy, "Song"),
                Slot("system", "System stats", ComponentState.Failed, null),
                Slot("clock", "Clock and date", ComponentState.Starting, null),
            ];
            view.ShowWidgets(slots, Render);
            var tabs = Tabs(view);
            var first = (view.SelectedWidgetId, Shown(view));

            view.SelectWidget("system");
            var second = (view.SelectedWidgetId, Shown(view));

            view.ShowWidgets(slots, Render); // An update keeps the selection.
            var kept = view.SelectedWidgetId;

            view.SelectAdjacentWidget(1); // Wraps around.
            return (tabs, first, second, kept, view.SelectedWidgetId, changes, ((FrameworkElement)view.FindName("WidgetPlaceholder")).Visibility);
        });

        Assert.Equal(["Tab.media", "Tab.system"], result.tabs); // The clock has nothing to show yet: no tab.
        Assert.Equal(("media", "Song"), result.first);
        Assert.Equal(("system", "System stats isn't available right now."), result.second);
        Assert.Equal("system", result.kept);
        Assert.Equal("media", result.Item5);
        Assert.Equal(3, result.changes);
        Assert.Equal(Visibility.Collapsed, result.Item7);
    }

    [Fact]
    public void A_single_widget_needs_no_tab_strip()
    {
        var visibility = _ui.Invoke(() =>
        {
            var view = new NotchView();
            view.ShowWidgets([Slot("media", "Media", ComponentState.Healthy, "Song")], Render);
            return ((FrameworkElement)view.FindName("TabStrip")).Visibility;
        });

        Assert.Equal(Visibility.Collapsed, visibility);
    }

    private static UIElement? Render(WidgetSlot slot) => slot.Snapshot is string text ? new TextBlock { Text = text } : null;

    private static string[] Tabs(NotchView view) =>
        [.. ((Panel)view.FindName("TabStrip")).Children.OfType<UIElement>().Select(AutomationProperties.GetAutomationId)];

    private static string Shown(NotchView view) => view.WidgetElements.OfType<TextBlock>().Single().Text;

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
