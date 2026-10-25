namespace DynamicIsland.UnitTests.TestSupport;

/// <summary>Clock whose timers fire only when <see cref="Advance"/> moves time past their due time (deterministic).</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];

    public DateTimeOffset UtcNow { get; private set; } = start;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public override long GetTimestamp() => UtcNow.UtcTicks;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    /// <summary>Moves time forward and synchronously fires every timer that becomes due, in due order.</summary>
    public void Advance(TimeSpan by)
    {
        var target = UtcNow + by;
        while (true)
        {
            ManualTimer? next;
            lock (_gate)
            {
                next = _timers.Where(t => t.Due is { } due && due <= target).MinBy(t => t.Due);
            }

            if (next is null)
            {
                break;
            }

            UtcNow = next.Due!.Value;
            next.Fire();
        }

        UtcNow = target;
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public DateTimeOffset? Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner.UtcNow + dueTime;
            _period = period;
            return true;
        }

        public void Fire()
        {
            Due = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero ? null : Due + _period;
            callback(state);
        }

        public void Dispose() => Due = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
