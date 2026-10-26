using System.Globalization;
using DynamicIsland.Core.Components;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using DynamicIsland.Widgets;

namespace DynamicIsland.UnitTests.Widgets;

/// <summary>The clock widget (P04-02, ADR 0019) on real time; waits are short and bounded.</summary>
public sealed class ClockWidgetTests : IDisposable
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private readonly Settings _settings = new();
    private readonly ClockWidget _widget;
    private readonly CancellationTokenSource _stop = new();

    public ClockWidgetTests() => _widget = new ClockWidget(_settings, TimeProvider.System, () => English);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _widget.Dispose();
    }

    [Fact]
    public async Task Publishes_at_once_with_the_users_clock_options_and_a_full_date()
    {
        _settings.Current = WithClock(new ClockSettings { DateFormat = ClockDateFormat.Weekday });

        var run = Start();
        var snapshot = await WaitForSnapshotAsync(_ => true);

        Assert.Equal(ClockFormatter.Format(snapshot.LocalTime, _settings.Current.Appearance.Clock, English), snapshot.Compact);
        Assert.Equal(ClockFormatter.Format(snapshot.LocalTime, new ClockSettings(), English), snapshot.Time);
        Assert.Equal(snapshot.LocalTime.ToString("dddd, MMMM d, yyyy", English), snapshot.Date);
        Assert.False(run.IsCompleted);
    }

    [Fact]
    public async Task Ticks_once_a_minute_by_default_never_per_frame()
    {
        _ = Start(); // Ends with cancellation in Dispose.
        await WaitForSnapshotAsync(_ => true);

        await Task.Delay(1500, Ct);

        Assert.InRange(_widget.Publishes, 1, 2); // 2 only if a minute boundary passed during the wait.
    }

    [Fact]
    public async Task Ticks_once_a_second_with_seconds_shown()
    {
        _settings.Current = WithClock(new ClockSettings { ShowSeconds = true });
        _ = Start(); // Ends with cancellation in Dispose.
        await WaitForSnapshotAsync(_ => true);

        await Task.Delay(2500, Ct);

        Assert.InRange(_widget.Publishes, 3, 5);
    }

    [Fact]
    public async Task Inactive_widget_does_not_tick_and_catches_up_when_active()
    {
        _settings.Current = WithClock(new ClockSettings { ShowSeconds = true });
        _ = Start(); // Ends with cancellation in Dispose.
        await WaitForSnapshotAsync(_ => true);

        _widget.SetActive(false);
        await Task.Delay(300, Ct);
        var before = _widget.Publishes;
        await Task.Delay(1500, Ct);
        Assert.Equal(before, _widget.Publishes);

        _widget.SetActive(true);
        await WaitUntilAsync(() => _widget.Publishes > before);
    }

    [Fact]
    public async Task Settings_and_system_changes_refresh_at_once()
    {
        _ = Start(); // Ends with cancellation in Dispose.
        await WaitForSnapshotAsync(_ => true);

        _settings.Change(WithClock(new ClockSettings { HourFormat = ClockHourFormat.TwentyFourHour, DateFormat = ClockDateFormat.Numeric }));
        var snapshot = await WaitForSnapshotAsync(s => s.Compact.Contains('/', StringComparison.Ordinal));
        Assert.DoesNotContain("M", snapshot.Compact, StringComparison.Ordinal); // No AM/PM in 24-hour time.

        var count = _widget.Publishes;
        _widget.OnSystemChanged();
        await WaitUntilAsync(() => _widget.Publishes > count);
    }

    [Fact]
    public async Task Stopping_ends_the_loop_and_release_drops_the_content()
    {
        var run = Start();
        await WaitForSnapshotAsync(_ => true);

        await _stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        _widget.Release();

        Assert.Null(_widget.Snapshot);
        Assert.Equal(0, _settings.Subscribers);
    }

    private static AppSettings WithClock(ClockSettings clock) => new() { Appearance = new AppearanceSettings { Clock = clock } };

    private Task Start() => Task.Run(() => _widget.RunAsync(new Context(), _stop.Token), Ct);

    private async Task<ClockSnapshot> WaitForSnapshotAsync(Func<ClockSnapshot, bool> condition)
    {
        ClockSnapshot? snapshot = null;
        await WaitUntilAsync(() => (snapshot = _widget.Snapshot as ClockSnapshot) is not null && condition(snapshot));
        return snapshot!;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition.");
            await Task.Delay(10, Ct);
        }
    }

    private sealed class Context : IComponentContext
    {
        public void ReportHealthy()
        {
        }

        public void ReportDegraded(string reasonCode)
        {
        }
    }

    private sealed class Settings : ISettingsService
    {
        private EventHandler<AppSettings>? _changed;

        public event EventHandler<AppSettings>? Changed
        {
            add
            {
                _changed += value;
                Subscribers++;
            }

            remove
            {
                _changed -= value;
                Subscribers--;
            }
        }

        public int Subscribers { get; private set; }

        public AppSettings Current { get; set; } = new();

        public bool IsReadOnly => false;

        public void Change(AppSettings settings)
        {
            Current = settings;
            _changed?.Invoke(this, settings);
        }

        public Task<SettingsSaveResult> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
