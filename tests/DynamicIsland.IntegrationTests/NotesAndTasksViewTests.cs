using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DynamicIsland.App;
using DynamicIsland.Core.Components;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Widgets;
using DynamicIsland.Infrastructure.PersonalData;
using DynamicIsland.TestSupport;
using DynamicIsland.Widgets;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicIsland.IntegrationTests;

/// <summary>Notes and tasks views (P04-06, ADR 0023) with the real registry, widgets and SQLite store.</summary>
public sealed class NotesAndTasksViewTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "DynamicIsland.IntegrationTests", Guid.NewGuid().ToString("N"));
    private readonly StaDispatcherThread _ui = new();
    private readonly LifecycleCoordinator _lifecycle = new(TimeProvider.System);
    private readonly SqlitePersonalDataStore _store;
    private readonly ComponentHost _host;
    private readonly WidgetRegistry _registry;
    private Window? _window;
    private WidgetPresenter? _presenter;

    public NotesAndTasksViewTests()
    {
        _store = new SqlitePersonalDataStore(_folder, TimeProvider.System, NullLogger<SqlitePersonalDataStore>.Instance);
        _host = new ComponentHost(_lifecycle, new InMemoryNotificationService(TimeProvider.System), new DefaultFailureClassifier(), LaunchArguments.Default, TimeProvider.System);
        _registry = new WidgetRegistry(_host, [new TasksWidget(_store), new NotesWidget(_store)]);
        _registry.Register([new WidgetSetting(WidgetIds.Tasks, true), new WidgetSetting(WidgetIds.Notes, true)]);
        _host.StartAll();
    }

    public void Dispose()
    {
        _ui.Invoke(() => _window?.Close());
        _ui.Dispose();
        _lifecycle.RequestShutdown(ExitReason.UserExit);
        _host.Dispose();
        _registry.Dispose();
        _store.Dispose();
        _lifecycle.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task Tasks_are_added_with_enter_completed_with_the_check_box_and_edited_with_f2()
    {
        var view = await ShowAsync(WidgetIds.Tasks);

        _ui.Invoke(() =>
        {
            var add = Find<TextBox>(view, "Tasks.Add");
            add.Text = "Water the plants";
            Press(add, Key.Enter);
            Assert.Equal(string.Empty, add.Text);
        });
        Assert.True(await RenderUntilAsync(view, () => Texts(view).Contains("Water the plants")), "The new task must appear.");

        var (row, check) = _ui.Invoke(() =>
        {
            var c = Descendants(view).OfType<CheckBox>().Single();
            return ((Grid)((FrameworkElement)c.Parent).Parent, c);
        });
        _ui.Invoke(() => check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)));
        Assert.True(await RenderUntilAsync(view, () => check.IsChecked == true), "Completing must be saved and shown.");
        Assert.Same(row, _ui.Invoke(() => (Grid)((FrameworkElement)Descendants(view).OfType<CheckBox>().Single().Parent).Parent)); // Row reused (the check sits in its focus ring).

        _ui.Invoke(() =>
        {
            Press(check, Key.F2);
            var editor = Descendants(view).OfType<TextBox>().Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Edit");
            editor.Text = "Water the plants on the balcony";
            Press(editor, Key.Enter);
        });
        Assert.True(await RenderUntilAsync(view, () => Texts(view).Contains("Water the plants on the balcony")), "The edit must be saved.");
    }

    [Fact]
    public async Task Notes_are_added_and_escape_in_the_box_clears_it_without_closing_the_island()
    {
        var view = await ShowAsync(WidgetIds.Notes);

        var escapeHandled = _ui.Invoke(() =>
        {
            var add = Find<TextBox>(view, "Notes.Add");
            add.Text = "never mind";
            var handled = Press(add, Key.Escape);
            Assert.Equal(string.Empty, add.Text);

            add.Text = "Call Sam at 5";
            Press(add, Key.Enter);
            return handled;
        });

        Assert.True(escapeHandled, "Esc in a non-empty box must be handled there (it must not collapse the island).");
        Assert.True(await RenderUntilAsync(view, () => Texts(view).Contains("Call Sam at 5")), "The note must appear.");
        Assert.Equal(1, await _store.CountNotesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Too_long_text_shows_a_message_and_saves_nothing()
    {
        var view = await ShowAsync(WidgetIds.Tasks);

        _ui.Invoke(() =>
        {
            var add = Find<TextBox>(view, "Tasks.Add");
            add.MaxLength = 0; // Bypass the box limit to reach the store's validation.
            add.Text = new string('x', 400);
            Press(add, Key.Enter);
        });

        Assert.True(await RenderUntilAsync(view, () => Texts(view).Any(t => t.StartsWith("Use 1–300", StringComparison.Ordinal))), "A refused task must explain why.");
        Assert.Equal((0, 0), await _store.CountTasksAsync(TestContext.Current.CancellationToken));
    }

    private async Task<FrameworkElement> ShowAsync(string widgetId)
    {
        Assert.True(await WaitAsync(() => _registry.Slots.Single(s => s.Id == widgetId).Snapshot is not null));
        return _ui.Invoke(() =>
        {
            _presenter = new WidgetPresenter(_registry, new NotchView(), null!, null!, _lifecycle, NullLogger<WidgetPresenter>.Instance);
            var element = (FrameworkElement)_presenter.ViewFor(_registry.Slots.Single(s => s.Id == widgetId))!;
            _window = new Window
            {
                Content = element,
                Width = 420,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                Tag = widgetId,
            };
            _window.Show();
            return element;
        });
    }

    /// <summary>Re-renders the view from the latest snapshot (the presenter does this on every change in the app).</summary>
    private async Task<bool> RenderUntilAsync(FrameworkElement view, Func<bool> condition)
    {
        var id = _ui.Invoke(() => (string)_window!.Tag);
        return await WaitAsync(() => _ui.Invoke(() =>
        {
            _presenter!.ViewFor(_registry.Slots.Single(s => s.Id == id));
            view.UpdateLayout();
            return condition();
        }));
    }

    private static bool Press(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target)!;
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(args);
        return args.Handled;
    }

    private static T Find<T>(DependencyObject root, string automationId)
        where T : FrameworkElement =>
        Descendants(root).OfType<T>().Single(e => System.Windows.Automation.AutomationProperties.GetAutomationId(e) == automationId);

    private static string[] Texts(DependencyObject root) => [.. Descendants(root).OfType<TextBlock>().Select(t => t.Text)];

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static async Task<bool> WaitAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(5))
            {
                return false;
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        return true;
    }
}
