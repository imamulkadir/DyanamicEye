using System.Windows;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Widgets;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App;

/// <summary>
/// Connects the <see cref="WidgetRegistry"/> to the island (ADR 0018): applies widget settings changes off the UI thread
/// and redraws the expanded panel on the UI thread whenever slots, statuses or content change (coalesced).
/// Each widget task adds its view to <see cref="Views"/>.
/// </summary>
public sealed class WidgetPresenter : IDisposable
{
    private readonly WidgetRegistry _registry;
    private readonly NotchView _view;
    private readonly ISettingsService _settings;
    private readonly LifecycleCoordinator _lifecycle;
    private readonly ILogger<WidgetPresenter> _logger;
    private readonly SemaphoreSlim _applying = new(1, 1);
    private IReadOnlyList<WidgetSetting> _applied = [];
    private int _renderQueued;

    public WidgetPresenter(WidgetRegistry registry, NotchView view, ISettingsService settings, LifecycleCoordinator lifecycle, ILogger<WidgetPresenter> logger)
    {
        _registry = registry;
        _view = view;
        _settings = settings;
        _lifecycle = lifecycle;
        _logger = logger;
    }

    /// <summary>Widget id → view for its <see cref="WidgetSlot.Snapshot"/> (null: nothing to show yet).</summary>
    public static IReadOnlyDictionary<string, Func<WidgetSlot, UIElement?>> Views { get; } = new Dictionary<string, Func<WidgetSlot, UIElement?>>(StringComparer.Ordinal);

    public void Start()
    {
        _applied = _settings.Current.Widgets;
        _registry.Changed += OnRegistryChanged;
        _settings.Changed += OnSettingsChanged;
        Render();
    }

    public void Dispose()
    {
        _registry.Changed -= OnRegistryChanged;
        _settings.Changed -= OnSettingsChanged;
        _applying.Dispose();
    }

    private void OnRegistryChanged(object? sender, EventArgs e)
    {
        // Snapshots can change often; one redraw per dispatcher turn is enough.
        if (Interlocked.Exchange(ref _renderQueued, 1) == 0)
        {
            _view.Dispatcher.BeginInvoke(() =>
            {
                Volatile.Write(ref _renderQueued, 0);
                Render();
            });
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

    private void Render() =>
        _view.ShowWidgets(_registry.Slots, slot => Views.TryGetValue(slot.Id, out var view) && slot.Snapshot is not null ? view(slot) : null);
}
