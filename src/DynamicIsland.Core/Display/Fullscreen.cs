using DynamicIsland.Core.Settings;

namespace DynamicIsland.Core.Display;

public enum FullscreenSource
{
    None,

    /// <summary>The foreground window covers its whole monitor (borderless/fullscreen app, F11 browser, video).</summary>
    WindowCoversMonitor,

    /// <summary>Windows reports a Direct3D exclusive-fullscreen app (game).</summary>
    Direct3DFullscreen,

    /// <summary>Windows reports presentation mode.</summary>
    PresentationMode,
}

/// <param name="MonitorId">Monitor of the fullscreen window (same id scheme as <see cref="MonitorInfo.Id"/>); never logged.</param>
/// <param name="ProcessExecutable">Executable file name of the fullscreen app, for exceptions only; never logged.</param>
public sealed record FullscreenSnapshot(bool IsFullscreen, FullscreenSource Source, string? MonitorId, string? ProcessExecutable)
{
    public static FullscreenSnapshot None { get; } = new(false, FullscreenSource.None, null, null);
}

/// <summary>Guide §3 contract: reports whether a fullscreen app is in front (D06).</summary>
public interface IFullscreenDetector
{
    /// <summary>Raised on the UI thread when <see cref="Current"/> changes.</summary>
    event EventHandler<FullscreenSnapshot>? Changed;

    FullscreenSnapshot Current { get; }
}

/// <summary>
/// D06: hide the island while a fullscreen app is in front <b>on the island's monitor</b>, unless auto-hide is off or the
/// app is listed in <see cref="BehaviorSettings.FullscreenExceptions"/>. When the monitor is unknown (exclusive fullscreen
/// reported without a window), hide — the island must never fight a game for the top of the screen.
/// </summary>
public static class FullscreenPolicy
{
    public static bool ShouldHide(FullscreenSnapshot snapshot, string? islandMonitorId, BehaviorSettings behavior)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(behavior);

        if (!behavior.AutoHideInFullscreen || !snapshot.IsFullscreen)
        {
            return false;
        }

        if (snapshot.ProcessExecutable is { } executable
            && behavior.FullscreenExceptions.Contains(executable, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return snapshot.MonitorId is null
            || islandMonitorId is null
            || string.Equals(snapshot.MonitorId, islandMonitorId, StringComparison.OrdinalIgnoreCase);
    }
}
