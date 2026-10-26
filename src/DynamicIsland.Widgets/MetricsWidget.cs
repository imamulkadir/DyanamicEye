using DynamicIsland.Core.Components;
using DynamicIsland.Core.Metrics;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Widgets;

namespace DynamicIsland.Widgets;

/// <param name="CpuPercent">Busy share since the previous sample, or <c>null</c> until two samples exist.</param>
/// <param name="Battery"><c>null</c> when unknown; <see cref="BatteryStatus.HasBattery"/> false on PCs without one.</param>
public sealed record MetricsSnapshot(double? CpuPercent, MemoryStatus? Memory, BatteryStatus? Battery, TimeSpan Uptime);

/// <summary>
/// CPU, memory, battery and uptime (guide P04-04, ADR 0021). Samples every <see cref="Interval"/> only while the island
/// is expanded and on screen; otherwise it does nothing. On expanding it shows memory, battery and uptime at once and
/// CPU after <see cref="BaselineDelay"/>, so CPU is never an average over a long idle gap.
/// </summary>
public sealed class MetricsWidget(ISystemMetricsService metrics, TimeProvider timeProvider) : IWidget, IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan BaselineDelay = TimeSpan.FromMilliseconds(500);

    private readonly Lock _gate = new();
    private CancellationTokenSource _wake = new();
    private MetricsSnapshot? _snapshot;
    private CpuTimes? _baseline;
    private bool _active = true;
    private bool _expanded;

    public event EventHandler? SnapshotChanged;

    public string Id => WidgetIds.SystemStats;

    public string Name => WidgetRegistry.ComponentName(WidgetIds.SystemStats);

    public object? Snapshot => Volatile.Read(ref _snapshot);

    /// <summary>Samples taken (tests: proves there is no sampling while collapsed).</summary>
    public int Samples { get; private set; }

    public async Task RunAsync(IComponentContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ReportHealthy();

        while (!cancellationToken.IsCancellationRequested)
        {
            CancellationToken wake;
            bool sampling;
            lock (_gate)
            {
                wake = _wake.Token;
                sampling = _active && _expanded;
                if (!sampling)
                {
                    _baseline = null; // The next CPU value starts from a fresh baseline.
                }
            }

            var delay = Timeout.InfiniteTimeSpan;
            if (sampling)
            {
                var hadBaseline = Sample();
                delay = hadBaseline ? Interval : BaselineDelay;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, wake);
            try
            {
                await Task.Delay(delay, timeProvider, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Woken: expanded, collapsed, hidden or resumed.
            }
        }
    }

    public void SetActive(bool active) => Change(() => _active = active);

    public void SetExpanded(bool expanded) => Change(() => _expanded = expanded);

    /// <summary>Resume from sleep: the CPU counters jumped, so start a new baseline.</summary>
    public void OnSystemChanged()
    {
        lock (_gate)
        {
            _baseline = null;
        }

        Wake();
    }

    public void Release()
    {
        lock (_gate)
        {
            _baseline = null;
        }

        Volatile.Write(ref _snapshot, null);
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _wake.Dispose();
        }
    }

    /// <returns>True when a CPU value could be computed from an earlier baseline.</returns>
    private bool Sample()
    {
        var cpu = metrics.GetCpuTimes();
        double? cpuPercent = null;
        lock (_gate)
        {
            if (_baseline is { } baseline && cpu is { } now)
            {
                cpuPercent = CpuUsage.Between(baseline, now);
            }

            _baseline = cpu;
        }

        Samples++;
        var snapshot = new MetricsSnapshot(
            cpuPercent,
            metrics.GetMemory(),
            metrics.GetBattery(),
            metrics.GetUptime());
        Volatile.Write(ref _snapshot, snapshot);
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
        return cpuPercent is not null;
    }

    private void Change(Action update)
    {
        lock (_gate)
        {
            update();
        }

        Wake();
    }

    private void Wake()
    {
        CancellationTokenSource previous;
        lock (_gate)
        {
            previous = _wake;
            _wake = new CancellationTokenSource();
        }

        // Not disposed here: the run loop may be linking to its token right now.
        previous.Cancel();
    }
}
