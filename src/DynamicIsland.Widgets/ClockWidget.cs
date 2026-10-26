using System.Globalization;
using DynamicIsland.Core.Components;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using DynamicIsland.Core.Widgets;

namespace DynamicIsland.Widgets;

/// <param name="LocalTime">The moment the texts describe.</param>
/// <param name="Compact">The notch text from the user's clock options (ADR 0016), e.g. "Mon · 2:42 PM".</param>
/// <param name="Time">The time alone for the expanded view, e.g. "2:42 PM".</param>
/// <param name="Date">The full date in the region format, e.g. "Monday, October 26, 2026".</param>
public sealed record ClockSnapshot(DateTime LocalTime, string Compact, string Time, string Date);

/// <summary>
/// Clock and date widget (guide P04-02, ADR 0019). Publishes a <see cref="ClockSnapshot"/> once per displayed change —
/// the next whole minute, or second when seconds are shown — never per frame. While inactive (island off screen) it
/// waits without ticking; settings, activation and system changes wake it at once. No countdown timers or stopwatch
/// (not approved for v1).
/// </summary>
public sealed class ClockWidget(ISettingsService settings, TimeProvider timeProvider, Func<CultureInfo>? culture = null) : IWidget, IDisposable
{
    private readonly Func<CultureInfo> _culture = culture ?? (() => CultureInfo.CurrentCulture);
    private readonly Lock _gate = new();
    private CancellationTokenSource _wake = new();
    private ClockSnapshot? _snapshot;
    private bool _active = true;

    public event EventHandler? SnapshotChanged;

    public string Id => WidgetIds.Clock;

    public string Name => WidgetRegistry.ComponentName(WidgetIds.Clock);

    public object? Snapshot => Volatile.Read(ref _snapshot);

    /// <summary>Number of snapshots published (tests and diagnostics: proves there is no per-frame work).</summary>
    public int Publishes { get; private set; }

    public async Task RunAsync(IComponentContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        settings.Changed += OnSettingsChanged;
        try
        {
            context.ReportHealthy();
            while (!cancellationToken.IsCancellationRequested)
            {
                CancellationToken wake;
                bool active;
                lock (_gate)
                {
                    wake = _wake.Token;
                    active = _active;
                }

                var delay = Timeout.InfiniteTimeSpan;
                if (active)
                {
                    var clock = settings.Current.Appearance.Clock;
                    var now = timeProvider.GetLocalNow().DateTime;
                    Publish(now, clock);
                    delay = ClockFormatter.DelayUntilNextChange(now, clock.ShowSeconds);
                }

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, wake);
                try
                {
                    await Task.Delay(delay, timeProvider, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Woken early: settings, activation or a system change.
                }
            }
        }
        finally
        {
            settings.Changed -= OnSettingsChanged;
        }
    }

    public void Release()
    {
        Volatile.Write(ref _snapshot, null);
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetActive(bool active)
    {
        lock (_gate)
        {
            if (_active == active)
            {
                return;
            }

            _active = active;
        }

        Wake();
    }

    public void OnSystemChanged() => Wake();

    private void OnSettingsChanged(object? sender, AppSettings e) => Wake();

    private void Wake()
    {
        CancellationTokenSource previous;
        lock (_gate)
        {
            previous = _wake;
            _wake = new CancellationTokenSource();
        }

        // Not disposed here: the run loop may be linking to its token right now. Cancelled sources hold no timers.
        previous.Cancel();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _wake.Dispose();
        }
    }

    private void Publish(DateTime now, ClockSettings clock)
    {
        var culture = _culture();
        var snapshot = new ClockSnapshot(
            now,
            ClockFormatter.Format(now, clock, culture),
            ClockFormatter.Format(now, clock with { DateFormat = ClockDateFormat.None }, culture),
            now.ToString(culture.DateTimeFormat.LongDatePattern, culture));
        Volatile.Write(ref _snapshot, snapshot);
        Publishes++;
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }
}
