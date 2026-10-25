namespace DynamicIsland.Core.Lifecycle;

public enum CrashDecision
{
    Restart,
    RestartInSafeMode,
    Stop,
}

/// <summary>
/// D03: at most <see cref="MaxAutomaticRestarts"/> automatic restarts within a rolling <see cref="Window"/>.
/// The next crash restarts once in Safe Mode; a crash in Safe Mode stops the app.
/// </summary>
public static class CrashPolicy
{
    public const int MaxAutomaticRestarts = 2;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <param name="crashes">Recorded crash times, including the crash being decided.</param>
    /// <param name="now">Current time.</param>
    /// <param name="crashedInSafeMode">Whether the run that just crashed was in Safe Mode.</param>
    public static CrashDecision Decide(IReadOnlyCollection<DateTimeOffset> crashes, DateTimeOffset now, bool crashedInSafeMode)
    {
        ArgumentNullException.ThrowIfNull(crashes);

        if (crashedInSafeMode)
        {
            return CrashDecision.Stop;
        }

        var recent = crashes.Count(t => IsRecent(t, now));
        return recent <= MaxAutomaticRestarts ? CrashDecision.Restart : CrashDecision.RestartInSafeMode;
    }

    /// <summary>Inside the rolling window and not in the future (clock changes).</summary>
    public static bool IsRecent(DateTimeOffset crash, DateTimeOffset now) => crash <= now && now - crash <= Window;
}
