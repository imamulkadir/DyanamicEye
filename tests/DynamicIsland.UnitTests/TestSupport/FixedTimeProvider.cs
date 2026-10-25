namespace DynamicIsland.UnitTests.TestSupport;

/// <summary>Deterministic, manually advanced clock; timers still use the system implementation.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
