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
    public void Until_the_user_picks_a_tab_the_first_tab_in_order_is_shown()
    {
        var selected = _ui.Invoke(() =>
        {
            var view = new NotchView();
            var media = Slot("media", "Media", ComponentState.Healthy, "Song");
            var system = Slot("system", "System stats", ComponentState.Healthy, "CPU");

            view.ShowWidgets([system], Render); // Stats publish before the media widget.
            view.ShowWidgets([media, system], Render);
            var automatic = view.SelectedWidgetId;

            view.SelectWidget("system");
            view.ShowWidgets([media, system], Render);
            var chosen = view.SelectedWidgetId;

            view.ShowWidgets([media], Render); // The chosen widget goes away: its choice is forgotten.
            view.ShowWidgets([media, system], Render);
            return (automatic, chosen, view.SelectedWidgetId);
        });

        Assert.Equal(("media", "system", "media"), selected);
    }

    [Fact]
    public void A_failed_widgets_tab_offers_try_again()
    {
        var requested = _ui.Invoke(() =>
        {
            var view = new NotchView();
            var ids = new List<string>();
            view.RetryRequested += (_, id) => ids.Add(id);
            view.ShowWidgets([Slot("system", "System stats", ComponentState.Failed, null)], Render);

            var retry = ((Panel)view.WidgetElements.Single()).Children.OfType<Button>().Single();
            retry.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            return (AutomationProperties.GetAutomationId(retry), (string)retry.Content, ids.Single());
        });

        Assert.Equal(("Retry.system", "Try again", "system"), requested);
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

    private static string Shown(NotchView view) => view.WidgetElements.Single() switch
    {
        TextBlock text => text.Text,
        Panel note => note.Children.OfType<TextBlock>().First().Text, // A failed widget's note (ADR 0027).
        var other => other.ToString()!,
    };

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
