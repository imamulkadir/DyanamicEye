using System.Globalization;
using System.Windows.Threading;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using Microsoft.Win32;

namespace DynamicIsland.App;

/// <summary>
/// Keeps the notch clock current (ADR 0016) with one timer tick per displayed change: once a minute, or once a second
/// when seconds are shown; no animation or polling (ADR 0011). Stops while the island is hidden, paused or suspended
/// for a fullscreen app, and catches up at once when it returns, the clock is changed, the time zone or region format
/// changes, or the PC resumes.
/// </summary>
public sealed class NotchClock : IDisposable
{
    private readonly NotchView _view;
    private readonly ISettingsService _settings;
    private readonly IIslandStateMachine _state;
    private readonly TimeProvider _time;
    private readonly DispatcherTimer _timer;

    public NotchClock(NotchView view, ISettingsService settings, IIslandStateMachine state, TimeProvider time)
    {
        _view = view;
        _settings = settings;
        _state = state;
        _time = time;
        _timer = new DispatcherTimer(DispatcherPriority.Normal, view.Dispatcher);
        _timer.Tick += (_, _) => Refresh();
    }

    /// <summary>True while a tick is scheduled (tests and diagnostics).</summary>
    public bool IsTicking => _timer.IsEnabled;

    /// <summary>Delay of the scheduled tick (tests and diagnostics).</summary>
    public TimeSpan NextTickIn => _timer.Interval;

    public void Start()
    {
        _settings.Changed += OnSettingsChanged;
        _state.StateChanged += OnStateChanged;
        SystemEvents.TimeChanged += OnSystemClockChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Refresh();
    }

    public void Dispose()
    {
        _timer.Stop();
        _settings.Changed -= OnSettingsChanged;
        _state.StateChanged -= OnStateChanged;
        SystemEvents.TimeChanged -= OnSystemClockChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    /// <summary>Updates the text now and schedules the next update (UI thread).</summary>
    public void Refresh()
    {
        _timer.Stop();
        var settings = _settings.Current;
        if (!IsClockEnabled(settings))
        {
            _view.SetClock(null);
            return;
        }

        var now = _time.GetLocalNow().DateTime;
        _view.SetClock(ClockFormatter.Format(now, settings.Appearance.Clock, CultureInfo.CurrentCulture));
        if (IsOnScreen(_state.State))
        {
            _timer.Interval = ClockFormatter.DelayUntilNextChange(now, settings.Appearance.Clock.ShowSeconds);
            _timer.Start();
        }
    }

    /// <summary>The clock is the clock widget's collapsed view: turning that widget off in settings hides it.</summary>
    private static bool IsClockEnabled(AppSettings settings) =>
        settings.Widgets.FirstOrDefault(w => w.Id == WidgetIds.Clock)?.Enabled ?? true;

    private static bool IsOnScreen(IslandState state) => IslandLayout.For(state) is not null;

    private void OnSettingsChanged(object? sender, AppSettings settings) => _view.Dispatcher.BeginInvoke(Refresh);

    private void OnStateChanged(object? sender, IslandStateChangedEventArgs e)
    {
        if (IsOnScreen(e.Current) != IsOnScreen(e.Previous))
        {
            _view.Dispatcher.BeginInvoke(Refresh);
        }
    }

    private void OnSystemClockChanged(object? sender, EventArgs e) => _view.Dispatcher.BeginInvoke(() =>
    {
        TimeZoneInfo.ClearCachedData();
        Refresh();
    });

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.Locale)
        {
            // The user changed the Windows region or time format.
            _view.Dispatcher.BeginInvoke(() =>
            {
                CultureInfo.CurrentCulture.ClearCachedData();
                Refresh();
            });
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _view.Dispatcher.BeginInvoke(Refresh);
        }
    }
}
