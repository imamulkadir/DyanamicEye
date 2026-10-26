using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Notifications;

namespace DynamicIsland.Core.Components;

/// <summary>
/// Owns all isolated components. Starts them in registration order (optional ones are skipped in Safe Mode)
/// and registers their shutdown with the <see cref="LifecycleCoordinator"/>. Components can be disabled and enabled
/// at run time (widgets, ADR 0018): a disabled component is never started and is stopped when disabled while running.
/// Pause/resume and enable/disable are serialized.
/// </summary>
public sealed class ComponentHost(
    LifecycleCoordinator lifecycle,
    INotificationService notices,
    IFailureClassifier classifier,
    LaunchArguments launchArguments,
    TimeProvider timeProvider) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _transitions = new(1, 1);
    private readonly List<Entry> _components = [];
    private bool _started;
    private bool _paused;

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
    /// <param name="enabled">A disabled component is registered but not started until <see cref="SetEnabledAsync"/>.</param>
    public void Add(IIsolatedComponent component, bool optional, RetryPolicy? policy = null, bool enabled = true)
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

            _components.Add(new Entry(boundary, optional) { Enabled = enabled });
        }
    }

    public ComponentStatus? StatusOf(string name)
    {
        lock (_gate)
        {
            return _components.FirstOrDefault(c => c.Boundary.Name == name)?.Boundary.Status;
        }
    }

    public void StartAll()
    {
        Entry[] components;
        lock (_gate)
        {
            if (_started)
            {
                throw new InvalidOperationException("The component host was already started.");
            }

            _started = true;
            components = [.. _components];
        }

        foreach (var entry in components)
        {
            if (IsSkipped(entry))
            {
                entry.Boundary.MarkSkippedForSafeMode();
                continue;
            }

            lifecycle.Register($"component:{entry.Boundary.Name}", entry.Boundary.StopAsync);
            if (entry.Enabled)
            {
                entry.Boundary.Start();
            }
            else
            {
                entry.Boundary.MarkDisabled();
            }
        }
    }

    /// <summary>
    /// Pauses (stops) or resumes all optional components that run in this mode (tray Pause, ADR 0013). Components
    /// skipped for Safe Mode stay skipped and disabled ones stay stopped. Stops are bounded by <paramref name="cancellationToken"/>.
    /// </summary>
    public async Task SetPausedAsync(bool paused, CancellationToken cancellationToken)
    {
        await _transitions.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Entry[] components;
            lock (_gate)
            {
                _paused = paused;
                if (!_started)
                {
                    return;
                }

                components = [.. _components];
            }

            foreach (var entry in components.Where(e => e.Optional && !IsSkipped(e) && e.Enabled))
            {
                if (paused)
                {
                    await entry.Boundary.StopAsync(cancellationToken).ConfigureAwait(false);
                    entry.Boundary.MarkPaused();
                }
                else if (!entry.Boundary.IsRunning)
                {
                    entry.Boundary.Start();
                }
            }
        }
        finally
        {
            _transitions.Release();
        }
    }

    /// <summary>
    /// Enables (starts, unless paused or skipped) or disables (stops, bounded by <paramref name="cancellationToken"/>) one
    /// component. Unknown names are ignored.
    /// </summary>
    /// <returns><c>true</c> when the component is now stopped by this call or was not running (safe to release its resources).</returns>
    public async Task<bool> SetEnabledAsync(string name, bool enabled, CancellationToken cancellationToken)
    {
        await _transitions.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Entry? entry;
            bool started;
            bool paused;
            lock (_gate)
            {
                entry = _components.FirstOrDefault(c => c.Boundary.Name == name);
                if (entry is null || entry.Enabled == enabled)
                {
                    return entry is not null && !enabled && !entry.Boundary.IsRunning;
                }

                entry.Enabled = enabled;
                started = _started;
                paused = _paused && entry.Optional;
            }

            if (!started || IsSkipped(entry))
            {
                return !enabled;
            }

            if (enabled)
            {
                if (paused)
                {
                    entry.Boundary.MarkPaused();
                }
                else
                {
                    entry.Boundary.Start();
                }

                return false;
            }

            await entry.Boundary.StopAsync(cancellationToken).ConfigureAwait(false);
            entry.Boundary.MarkDisabled();
            return true;
        }
        finally
        {
            _transitions.Release();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var entry in _components)
            {
                entry.Boundary.Dispose();
            }
        }

        _transitions.Dispose();
    }

    private bool IsSkipped(Entry entry) => entry.Optional && launchArguments.SafeMode;

    private sealed class Entry(ComponentBoundary boundary, bool optional)
    {
        public ComponentBoundary Boundary { get; } = boundary;

        public bool Optional { get; } = optional;

        public bool Enabled { get; set; }
    }
}
