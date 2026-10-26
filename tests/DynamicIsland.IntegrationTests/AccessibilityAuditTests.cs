using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DynamicIsland.App;
using DynamicIsland.Core.Components;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Media;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.PersonalData;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using DynamicIsland.Core.Widgets;
using DynamicIsland.TestSupport;
using DynamicIsland.Widgets;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicIsland.IntegrationTests;

/// <summary>
/// Accessibility audit of every real widget view in the expanded island (P05-03, ADR 0028): each control a keyboard or
/// pointer can reach has a screen-reader name and is big enough to hit.
/// </summary>
public sealed class AccessibilityAuditTests : IDisposable
{
    private static readonly ComponentStatus Healthy = new("x", ComponentState.Healthy);

    private readonly StaDispatcherThread _ui = new();

    public void Dispose() => _ui.Dispose();

    [Fact]
    public void Every_control_on_every_tab_has_a_name_and_a_big_enough_target()
    {
        var problems = _ui.Invoke(() =>
        {
            using var lifecycle = new LifecycleCoordinator(TimeProvider.System);
            using var host = new ComponentHost(lifecycle, new InMemoryNotificationService(TimeProvider.System), new DefaultFailureClassifier(), LaunchArguments.Default, TimeProvider.System);
            using var registry = new WidgetRegistry(host, []);
            var view = new NotchView { Width = 420, Height = 360 };
            var presenter = new WidgetPresenter(registry, view, null!, null!, lifecycle, NullLogger<WidgetPresenter>.Instance);
            view.ApplyVisualState(IslandState.Pinned, 360, 1.0);
            view.ShowWidgets(Slots(), presenter.ViewFor);

            var found = new List<string>();
            var checkedControls = 0;
            foreach (var id in WidgetIds.All)
            {
                view.SelectWidget(id);
                view.Measure(new Size(420, 2000));
                view.Arrange(new Rect(0, 0, 420, 2000));
                view.UpdateLayout();
                foreach (var control in Descendants(view).OfType<Control>().Where(IsInteractive))
                {
                    checkedControls++;
                    var name = UIElementAutomationPeer.CreatePeerForElement(control)?.GetName();
                    var where = $"{id}: {control.GetType().Name} '{name}'";
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        found.Add($"{where} has no screen-reader name");
                    }

                    var minimum = IsPrimary(control) ? IslandTheme.MinPrimaryTarget : IslandTheme.MinTarget;
                    if (control.ActualWidth < minimum || control.ActualHeight < minimum)
                    {
                        found.Add($"{where} is {control.ActualWidth:F0}×{control.ActualHeight:F0}, below {minimum}");
                    }
                }
            }

            return (found.Distinct().ToArray(), checkedControls);
        });

        Assert.Empty(problems.Item1);
        Assert.True(problems.checkedControls >= 20, $"Only {problems.checkedControls} controls were checked.");
    }

    private static bool IsInteractive(Control control) =>
        control is ButtonBase or Slider or TextBox && control.Focusable && IsShown(control);

    /// <summary>Offscreen, <see cref="UIElement.IsVisible"/> is always false: check the element and its parents instead.</summary>
    private static bool IsShown(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: not Visibility.Visible })
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The island's main controls: tabs, pin and the media and volume buttons.</summary>
    private static bool IsPinOrTab(Control control) => control is RadioButton || control.Name == "PinButton";

    private static bool IsPrimary(Control control) =>
        IsPinOrTab(control) || (System.Windows.Automation.AutomationProperties.GetAutomationId(control) is { } id && (id.StartsWith("Media.", StringComparison.Ordinal) || id == "Volume.Mute"));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static WidgetSlot[] Slots()
    {
        var now = DateTimeOffset.Now;
        return
        [
            new(WidgetIds.Clock, "Clock and date", Healthy, new ClockSnapshot(new DateTime(2026, 10, 26, 14, 42, 0), "2:42 PM", "2:42 PM", "Monday, October 26, 2026")),
            new(WidgetIds.Media, "Media", Healthy, new MediaSnapshot("Song", "Artist", MediaPlaybackState.Playing, MediaControls.Pause | MediaControls.Next | MediaControls.Previous, 0, null, null)),
            new(WidgetIds.SystemStats, "System stats", Healthy, new MetricsSnapshot(12, null, null, TimeSpan.FromHours(3))),
            new(WidgetIds.Volume, "Volume", Healthy, new VolumeSnapshot(true, 40, false)),
            new(WidgetIds.Tasks, "Tasks", Healthy, new TasksSnapshot([new TaskItem(1, "Buy milk", false, now, now, null), new TaskItem(2, "Done one", true, now, now, now)], 1, 1, false, null)),
            new(WidgetIds.Notes, "Notes", Healthy, new NotesSnapshot([new Note(1, "A note", now, now)], 1, false, null)),
        ];
    }
}
