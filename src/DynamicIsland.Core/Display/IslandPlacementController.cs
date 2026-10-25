namespace DynamicIsland.Core.Display;

/// <summary>
/// Re-evaluates placement against the current monitors on every display change. Because the stored preference is
/// never changed here, the island falls back while the preferred monitor is absent and returns once it reappears.
/// </summary>
public sealed class IslandPlacementController(IMonitorService monitors, Func<PlacementRequest> request)
{
    public PlacementResult? Current { get; private set; }

    /// <returns>The new placement, or <c>null</c> when no monitor is available (keep the window where it is).</returns>
    public PlacementUpdate? Update()
    {
        var available = monitors.GetMonitors();
        if (available.Count == 0)
        {
            return null;
        }

        var previous = Current;
        var result = IslandPlacementCalculator.Place(available, request());
        Current = result;

        var monitorChanged = previous is null || !string.Equals(previous.Monitor.Id, result.Monitor.Id, StringComparison.OrdinalIgnoreCase);
        var changed = monitorChanged || previous!.Rect != result.Rect || previous.UsedFallback != result.UsedFallback;
        return new PlacementUpdate(result, changed, monitorChanged);
    }
}

/// <param name="Changed">Target rectangle, monitor or fallback state differs from the previous update.</param>
public sealed record PlacementUpdate(PlacementResult Result, bool Changed, bool MonitorChanged);
