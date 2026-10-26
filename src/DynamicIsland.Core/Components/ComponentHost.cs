using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Notifications;

namespace DynamicIsland.Core.Components;

/// <summary>
/// Owns all isolated components. Starts them in registration order (optional ones are skipped in Safe Mode)
/// and registers their shutdown with the <see cref="LifecycleCoordinator"/>.
/// </summary>
public sealed class ComponentHost(
    LifecycleCoordinator lifecycle,
    INotificationService notices,
    IFailureClassifier classifier,
    LaunchArguments launchArguments,
    TimeProvider timeProvider) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<(ComponentBoundary Boundary, bool Optional)> _components = [];
    private bool _started;

    /// <summary>Forwarded from every component, on its background thread.</summary>
    public event EventHandler<ComponentStatus>? StatusChanged;

    public IReadOnlyList<ComponentStatus> Statuses
    {
        get
        {
            lock (_gate)
            {
                return _components.Select(c => c.Boundary.Status).ToArray();
            }
        }
    }

    /// <param name="optional">Optional components (widgets, data providers) do not run in Safe Mode.</param>
    public void Add(IIsolatedComponent component, bool optional, RetryPolicy? policy = null)
    {
        var boundary = new ComponentBoundary(component, lifecycle, notices, classifier, policy ?? RetryPolicy.Default, timeProvider);
        boundary.StatusChanged += (_, status) => StatusChanged?.Invoke(this, status);

        lock (_gate)
        {
            if (_started)
            {
                throw new InvalidOperationException("Components must be added before the host starts.");
            }

            if (_components.Any(c => c.Boundary.Name == component.Name))
            {
                throw new ArgumentException($"A component named '{component.Name}' is already registered.", nameof(component));
            }

            _components.Add((boundary, optional));
        }
    }

    public void StartAll()
    {
        (ComponentBoundary Boundary, bool Optional)[] components;
        lock (_gate)
        {
            if (_started)
            {
                throw new InvalidOperationException("The component host was already started.");
            }

            _started = true;
            components = [.. _components];
        }

        foreach (var (boundary, optional) in components)
        {
            if (optional && launchArguments.SafeMode)
            {
                boundary.MarkSkippedForSafeMode();
                continue;
            }

            lifecycle.Register($"component:{boundary.Name}", boundary.StopAsync);
            boundary.Start();
        }
    }

    /// <summary>
    /// Pauses (stops) or resumes all optional components that run in this mode (tray Pause, ADR 0013). Components
    /// skipped for Safe Mode stay skipped. Stops are bounded by <paramref name="cancellationToken"/>.
    /// </summary>
    public async Task SetPausedAsync(bool paused, CancellationToken cancellationToken)
    {
        (ComponentBoundary Boundary, bool Optional)[] components;
        lock (_gate)
        {
            if (!_started)
            {
                return;
            }

            components = [.. _components];
        }

        foreach (var (boundary, optional) in components)
        {
            if (!optional || launchArguments.SafeMode)
            {
                continue;
            }

            if (paused)
            {
                await boundary.StopAsync(cancellationToken).ConfigureAwait(false);
                boundary.MarkPaused();
            }
            else
            {
                boundary.Start();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var (boundary, _) in _components)
            {
                boundary.Dispose();
            }
        }
    }
}
