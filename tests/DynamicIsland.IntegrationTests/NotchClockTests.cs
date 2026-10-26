using System.Globalization;
using DynamicIsland.App;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using DynamicIsland.TestSupport;

namespace DynamicIsland.IntegrationTests;

/// <summary>The notch clock (ADR 0016) on a real <see cref="NotchView"/> with a fixed time.</summary>
public sealed class NotchClockTests : IDisposable
{
    private static readonly DateTimeOffset Afternoon = new(2026, 10, 26, 14, 42, 5, TimeSpan.Zero);

    private readonly StaDispatcherThread _ui = new();
    private readonly Settings _settings = new();
    private readonly States _state = new();

    public void Dispose() => _ui.Dispose();

    [Fact]
    public void Shows_the_time_and_ticks_once_a_minute_by_default()
    {
        var (text, ticking, interval) = Run(clock => clock.Refresh());

        Assert.Equal(Expected(new ClockSettings()), text);
        Assert.True(ticking);
        Assert.InRange(interval, TimeSpan.FromSeconds(54.9), TimeSpan.FromSeconds(55.1));
    }

    [Fact]
    public void Seconds_and_date_follow_settings_and_tick_every_second()
    {
        var clock = new ClockSettings { ShowSeconds = true, DateFormat = ClockDateFormat.WeekdayAndDate };
        _settings.Current = new AppSettings { Appearance = new AppearanceSettings { Clock = clock } };

        var (text, ticking, interval) = Run(c => c.Refresh());

        Assert.Equal(Expected(clock), text);
        Assert.True(ticking);
        Assert.True(interval <= TimeSpan.FromSeconds(1.05), $"Interval {interval}.");
    }

    [Fact]
    public void Turning_the_clock_widget_off_hides_it_and_stops_the_timer()
    {
        _settings.Current = new AppSettings { Widgets = [.. WidgetIds.All.Select(id => new WidgetSetting(id, id != WidgetIds.Clock))] };

        var (text, ticking, _) = Run(c => c.Refresh());

        Assert.Equal(string.Empty, text);
        Assert.False(ticking);
    }

    [Theory]
    [InlineData(IslandState.Hidden)]
    [InlineData(IslandState.Paused)]
    [InlineData(IslandState.SuspendedFullscreen)]
    public void No_ticks_while_the_island_is_off_screen(IslandState state)
    {
        _state.State = state;

        var (text, ticking, _) = Run(c => c.Refresh());

        Assert.NotEmpty(text);
        Assert.False(ticking);
    }

    private static string Expected(ClockSettings clock) =>
        ClockFormatter.Format(Afternoon.UtcDateTime, clock, CultureInfo.CurrentCulture);

    private (string Text, bool Ticking, TimeSpan Interval) Run(Action<NotchClock> act) => _ui.Invoke(() =>
    {
        var view = new NotchView();
        using var clock = new NotchClock(view, _settings, _state, new FixedTime(Afternoon));
        act(clock);
        return (view.ClockDisplay, clock.IsTicking, clock.NextTickIn);
    });

    /// <summary>A fixed instant in a UTC "local" zone, so the expected text does not depend on the host's time zone.</summary>
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class Settings : ISettingsService
    {
        public event EventHandler<AppSettings>? Changed
        {
            add { }
            remove { }
        }

        public AppSettings Current { get; set; } = new();

        public bool IsReadOnly => false;

        public Task<SettingsSaveResult> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class States : IIslandStateMachine
    {
        public event EventHandler<IslandStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public IslandState State { get; set; } = IslandState.Collapsed;

        public void Fire(IslandInput input) => throw new NotSupportedException();
    }
}
