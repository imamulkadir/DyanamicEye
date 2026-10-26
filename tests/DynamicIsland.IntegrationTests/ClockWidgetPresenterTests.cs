using System.Diagnostics;
using System.Windows.Controls;
using DynamicIsland.App;
using DynamicIsland.Core.Components;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using DynamicIsland.Core.Widgets;
using DynamicIsland.TestSupport;
using DynamicIsland.Widgets;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicIsland.IntegrationTests;

/// <summary>
/// The clock widget through the real registry, presenter and <see cref="NotchView"/> (P04-02, ADR 0019): the notch shows
/// its compact text, the expanded panel its large time and date, and turning the widget off clears both.
/// </summary>
public sealed class ClockWidgetPresenterTests : IDisposable
{
    private readonly StaDispatcherThread _ui = new();
    private readonly LifecycleCoordinator _lifecycle = new(TimeProvider.System);
    private readonly Settings _settings = new();

    public void Dispose()
    {
        _ui.Dispose();
        _lifecycle.Dispose();
    }

    [Fact]
    public async Task Notch_and_expanded_panel_show_the_clock_and_disabling_clears_them()
    {
        using var host = new ComponentHost(_lifecycle, new InMemoryNotificationService(TimeProvider.System), new DefaultFailureClassifier(), LaunchArguments.Default, TimeProvider.System);
        using var registry = new WidgetRegistry(host, BuiltInWidgets.Create(_settings, TimeProvider.System, new NoMedia(), new Platform.Windows.Metrics.Win32SystemMetrics()));
        var view = _ui.Invoke(() => new NotchView());
        using var presenter = new WidgetPresenter(registry, view, _settings, new States(), _lifecycle, NullLogger<WidgetPresenter>.Instance);

        registry.Register(_settings.Current.Widgets);
        host.StartAll();
        _ui.Invoke(presenter.Start);

        Assert.True(await WaitAsync(() => _ui.Invoke(() => view.ClockDisplay.Length > 0)), "The notch must show the time.");
        var (notch, panel) = _ui.Invoke(() => (view.ClockDisplay, view.WidgetElements.OfType<StackPanel>().Single()));
        var snapshot = (ClockSnapshot)registry.Slots.Single(s => s.Id == WidgetIds.Clock).Snapshot!;
        Assert.Equal(snapshot.Compact, notch);
        Assert.Equal([snapshot.Time, snapshot.Date], _ui.Invoke(() => panel.Children.OfType<TextBlock>().Select(t => t.Text).ToArray()));

        await registry.ApplyAsync([.. WidgetIds.All.Select(id => new WidgetSetting(id, id != WidgetIds.Clock))], TestContext.Current.CancellationToken);

        Assert.True(await WaitAsync(() => _ui.Invoke(() => view.ClockDisplay.Length == 0 && view.WidgetElements.Count == 0)), "Turning the clock off must clear it.");
        Assert.DoesNotContain(registry.Slots, s => s.Id == WidgetIds.Clock);
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

    private sealed class Settings : ISettingsService
    {
        public event EventHandler<AppSettings>? Changed
        {
            add { }
            remove { }
        }

        public AppSettings Current { get; } = new();

        public bool IsReadOnly => false;

        public Task<SettingsSaveResult> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoMedia : Core.Media.IMediaSessionService
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public Task<bool> StartAsync(CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<Core.Media.MediaInfo?> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult<Core.Media.MediaInfo?>(null);

        public Task<byte[]?> GetThumbnailAsync(int maxBytes, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);

        public Task<bool> SendAsync(Core.Media.MediaCommand command, CancellationToken cancellationToken) => Task.FromResult(false);

        public void Dispose()
        {
        }
    }

    private sealed class States : IIslandStateMachine
    {
        public event EventHandler<IslandStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public IslandState State => IslandState.Collapsed;

        public void Fire(IslandInput input) => throw new NotSupportedException();
    }
}
