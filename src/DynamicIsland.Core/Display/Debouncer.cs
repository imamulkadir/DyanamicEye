namespace DynamicIsland.Core.Display;

/// <summary>
/// Runs <paramref name="callback"/> once after <paramref name="quietPeriod"/> without further <see cref="Trigger"/> calls.
/// Used to coalesce bursts of display, DPI and work-area notifications. The callback runs on a timer thread.
/// </summary>
public sealed class Debouncer(TimeSpan quietPeriod, TimeProvider timeProvider, Action callback) : IDisposable
{
    private readonly Lock _gate = new();
    private ITimer? _timer;
    private bool _disposed;

    public void Trigger()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _timer ??= timeProvider.CreateTimer(_ => Fire(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timer.Change(quietPeriod, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Fire()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        callback();
    }
}
