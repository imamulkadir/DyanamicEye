namespace DynamicIsland.UnitTests.TestSupport;

/// <summary>Deterministic clock; timers still use the system implementation.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
