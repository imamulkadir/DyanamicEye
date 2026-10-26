using System.Globalization;
using System.Windows;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using DynamicIsland.Core.Widgets;
using DynamicIsland.Widgets;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DynamicIsland.App;

/// <summary>
/// Connects the <see cref="WidgetRegistry"/> to the island (ADR 0018): applies widget settings changes off the UI thread,
/// redraws on the UI thread whenever slots, statuses or content change (coalesced), shows the clock widget's compact
/// text in the notch (ADR 0016/0019), makes widgets inactive while the island is off screen, and forwards time,
/// time-zone, region and resume changes.
/// </summary>
public sealed class WidgetPresenter : IDisposable
{
    private readonly WidgetRegistry _registry;
    private readonly NotchView _view;
    private readonly ISettingsService _settings;
    private readonly IIslandStateMachine _state;
    private readonly LifecycleCoordinator _lifecycle;
    private readonly ILogger<WidgetPresenter> _logger;
    private readonly SemaphoreSlim _applying = new(1, 1);
    private IReadOnlyList<WidgetSetting> _applied = [];
    private int _renderQueued;

    public WidgetPresenter(
        WidgetRegistry registry,
        NotchView view,
        ISettingsService settings,
        IIslandStateMachine state,
        LifecycleCoordinator lifecycle,
        ILogger<WidgetPresenter> logger)
    {
        _registry = registry;
        _view = view;
        _settings = settings;
        _state = state;
        _lifecycle = lifecycle;
        _logger = logger;
    }

    /// <summary>Widget id → factory of its persistent expanded view (ADR 0022).</summary>
    public static IReadOnlyDictionary<string, Func<WidgetPresenter, WidgetView>> ViewFactories { get; } = new Dictionary<string, Func<WidgetPresenter, WidgetView>>(StringComparer.Ordinal)
    {
        [WidgetIds.Clock] = _ => new ClockView(),
        [WidgetIds.Media] = presenter => new MediaView(presenter),
        [WidgetIds.SystemStats] = _ => new MetricsView(),
        [WidgetIds.Volume] = presenter => new VolumeView(presenter),
        [WidgetIds.Tasks] = presenter => new TasksView(presenter),
        [WidgetIds.Notes] = presenter => new NotesView(presenter),
    };

    private readonly Dictionary<string, WidgetView> _views = new(StringComparer.Ordinal);

    /// <summary>
    /// The expanded view element for <paramref name="slot"/>, updated with its snapshot; <c>null</c> when the widget has
    /// nothing to show yet or no view. The same element is returned for the same widget every time (UI thread).
    /// </summary>
    public UIElement? ViewFor(WidgetSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (slot.Snapshot is null || !ViewFactories.TryGetValue(slot.Id, out var factory))
        {
            return null;
        }

        if (!_views.TryGetValue(slot.Id, out var view))
        {
            view = factory(this);
            _views.Add(slot.Id, view);
        }

        view.Update(slot.Snapshot);
        return view.Element;
    }

    /// <summary>Decoded album art; decoding happens off the UI thread and triggers a redraw.</summary>
    public AlbumArtCache AlbumArt { get; } = new();

    /// <summary>Runs a widget command from a view (button). Failures are logged by type and never reach the UI thread.</summary>
    public async void Execute(string widgetId, string command)
    {
        try
        {
            await _registry.ExecuteAsync(widgetId, command, _lifecycle.ApplicationStopping).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            AppLog.WidgetCommandFailed(_logger, widgetId, ex.GetType().Name);
        }
    }

    public void Start()
    {
        _applied = _settings.Current.Widgets;
        _registry.Changed += OnRegistryChanged;
        AlbumArt.Decoded += OnRegistryChanged;
        _settings.Changed += OnSettingsChanged;
        _state.StateChanged += OnStateChanged;
        SystemEvents.TimeChanged += OnTimeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _registry.SetActive(IsOnScreen(_state.State));
        _registry.SetExpanded(IsExpanded(_state.State));
        Render();
    }

    public void Dispose()
    {
        _registry.Changed -= OnRegistryChanged;
        AlbumArt.Decoded -= OnRegistryChanged;
        _settings.Changed -= OnSettingsChanged;
        _state.StateChanged -= OnStateChanged;
        SystemEvents.TimeChanged -= OnTimeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _applying.Dispose();
    }

    /// <summary>Redraws now (UI thread; tests).</summary>
    public void Render()
    {
        var slots = _registry.Slots;
        _view.ShowWidgets(slots, ViewFor);

        // The notch shows the clock widget's compact text; nothing when it is off, failed or skipped (Safe Mode).
        var clock = slots.FirstOrDefault(s => s.Id == WidgetIds.Clock);
        _view.SetClock(clock is { IsUnavailable: false, Snapshot: ClockSnapshot snapshot } ? snapshot.Compact : null);
    }

    private static bool IsOnScreen(IslandState state) => IslandLayout.For(state) is not null;

    private static bool IsExpanded(IslandState state) => state is IslandState.Expanded or IslandState.Pinned;


    private void OnRegistryChanged(object? sender, EventArgs e)
    {
        // Snapshots can change often (every second with seconds shown); one redraw per dispatcher turn is enough.
        if (Interlocked.Exchange(ref _renderQueued, 1) == 0)
        {
            _view.Dispatcher.BeginInvoke(() =>
            {
                Volatile.Write(ref _renderQueued, 0);
                Render();
            });
        }
    }

    private void OnStateChanged(object? sender, IslandStateChangedEventArgs e)
    {
        if (IsOnScreen(e.Current) != IsOnScreen(e.Previous))
        {
            _registry.SetActive(IsOnScreen(e.Current));
        }

        if (IsExpanded(e.Current) != IsExpanded(e.Previous))
        {
            _registry.SetExpanded(IsExpanded(e.Current));
        }
    }

    private void OnTimeChanged(object? sender, EventArgs e)
    {
        TimeZoneInfo.ClearCachedData();
        _registry.NotifySystemChanged();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.Locale)
        {
            // The user changed the Windows region or time format.
            CultureInfo.CurrentCulture.ClearCachedData();
            _registry.NotifySystemChanged();
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _registry.NotifySystemChanged();
        }
    }

    private async void OnSettingsChanged(object? sender, AppSettings settings)
    {
        try
        {
            await _applying.WaitAsync(_lifecycle.ApplicationStopping).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (settings.Widgets.SequenceEqual(_applied))
            {
                return;
            }

            _applied = settings.Widgets;
            await _registry.ApplyAsync(settings.Widgets, _lifecycle.ApplicationStopping).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A widget did not stop within WidgetRegistry.StopTimeout, or the app is exiting; its boundary records it.
            AppLog.WidgetChangeIncomplete(_logger);
        }
        finally
        {
            _applying.Release();
        }
    }
}
