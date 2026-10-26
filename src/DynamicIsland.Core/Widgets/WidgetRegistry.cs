using DynamicIsland.Core.Components;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;

namespace DynamicIsland.Core.Widgets;

/// <summary>
/// A built-in widget (guide §3 <c>IWidget</c>, ADR 0018): an isolated component whose background work runs in
/// <see cref="IIsolatedComponent.RunAsync"/> and whose latest content is <see cref="Snapshot"/>.
/// </summary>
public interface IWidget : IIsolatedComponent
{
    /// <summary>A <see cref="WidgetIds"/> value.</summary>
    string Id { get; }

    /// <summary>Immutable content for the UI, or <c>null</c> while there is nothing to show.</summary>
    object? Snapshot { get; }

    /// <summary>Raised (on any thread) after <see cref="Snapshot"/> changed.</summary>
    event EventHandler? SnapshotChanged;

    /// <summary>Drops cached content and resources after the widget stopped (disabled). <see cref="Snapshot"/> becomes <c>null</c>.</summary>
    void Release();
}

/// <summary>What the island shows for one enabled widget, in the user's order.</summary>
public sealed record WidgetSlot(string Id, string Label, ComponentStatus Status, object? Snapshot)
{
    /// <summary>The widget gave up after repeated failures; the UI shows a short "not available" note.</summary>
    public bool IsUnavailable => Status.State == ComponentState.Failed;
}

/// <summary>
/// Owns the compiled-in widgets (ADR 0018): registers them with the <see cref="ComponentHost"/> using the saved
/// enabled flags, applies settings changes (enable, disable and release, reorder) and publishes the ordered
/// <see cref="Slots"/>. Unknown or not-yet-built ids in settings are not shown.
/// </summary>
public sealed class WidgetRegistry
{
    /// <summary>Upper bound for stopping a disabled widget; it is marked timed out by its boundary after that.</summary>
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

    private readonly ComponentHost _host;
    private readonly RetryPolicy? _policy;
    private readonly IReadOnlyDictionary<string, IWidget> _widgets;
    private readonly Lock _gate = new();
    private IReadOnlyList<WidgetSetting> _settings = [];

    /// <param name="policy">Retry policy for every widget; <c>null</c> uses <see cref="RetryPolicy.Default"/>.</param>
    public WidgetRegistry(ComponentHost host, IEnumerable<IWidget> widgets, RetryPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(widgets);

        _host = host;
        _policy = policy;
        var byId = new Dictionary<string, IWidget>(StringComparer.Ordinal);
        foreach (var widget in widgets)
        {
            if (!WidgetIds.IsKnown(widget.Id) || widget.Name != ComponentName(widget.Id))
            {
                throw new ArgumentException($"Widget '{widget.Id}' must use a known id and the component name '{ComponentName(widget.Id)}'.", nameof(widgets));
            }

            if (!byId.TryAdd(widget.Id, widget))
            {
                throw new ArgumentException($"Widget '{widget.Id}' is registered twice.", nameof(widgets));
            }

            widget.SnapshotChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        }

        _widgets = byId;
        host.StatusChanged += (_, status) =>
        {
            if (_widgets.Values.Any(w => w.Name == status.Name))
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>Raised (on any thread) when a slot's order, status or content may have changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Compiled-in widget ids (diagnostics, tests).</summary>
    public IReadOnlyCollection<string> AvailableIds => [.. _widgets.Keys];

    /// <summary>Enabled widgets in the user's order.</summary>
    public IReadOnlyList<WidgetSlot> Slots
    {
        get
        {
            IReadOnlyList<WidgetSetting> settings;
            lock (_gate)
            {
                settings = _settings;
            }

            return [.. settings
                .Where(s => s.Enabled && _widgets.ContainsKey(s.Id))
                .Select(s => _widgets[s.Id])
                .Select(w => new WidgetSlot(
                    w.Id,
                    SettingsEditor.WidgetLabel(w.Id),
                    _host.StatusOf(w.Name) ?? new ComponentStatus(w.Name, ComponentState.NotStarted),
                    w.Snapshot))];
        }
    }

    public static string ComponentName(string widgetId) => "widget-" + widgetId;

    /// <summary>Registers every widget as an optional component (call before <see cref="ComponentHost.StartAll"/>).</summary>
    public void Register(IReadOnlyList<WidgetSetting> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_gate)
        {
            _settings = settings;
        }

        foreach (var widget in _widgets.Values)
        {
            _host.Add(widget, optional: true, _policy, IsEnabled(settings, widget.Id));
        }
    }

    /// <summary>Applies changed widget settings: stops and releases newly disabled widgets, starts enabled ones, reorders.</summary>
    public async Task ApplyAsync(IReadOnlyList<WidgetSetting> settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_gate)
        {
            _settings = settings;
        }

        foreach (var widget in _widgets.Values)
        {
            var enabled = IsEnabled(settings, widget.Id);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(StopTimeout);
            if (await _host.SetEnabledAsync(widget.Name, enabled, timeout.Token).ConfigureAwait(false))
            {
                widget.Release();
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A widget missing from settings counts as enabled (the validator appends missing built-ins as enabled).</summary>
    private static bool IsEnabled(IReadOnlyList<WidgetSetting> settings, string id) =>
        settings.FirstOrDefault(s => s.Id == id)?.Enabled ?? true;
}
