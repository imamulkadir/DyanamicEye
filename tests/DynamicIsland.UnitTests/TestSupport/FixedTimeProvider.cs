namespace DynamicIsland.UnitTests.TestSupport;

/// <summary>
/// Manually advanced clock. Both wall time and timestamps (elapsed-time measurement) follow <see cref="UtcNow"/>;
/// timers still use the system implementation.
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public override long GetTimestamp() => UtcNow.UtcTicks;
}
