using DynamicIsland.Core.Display;

namespace DynamicIsland.Core.Shell;

public enum TrayCommand
{
    ToggleVisibility,
    TogglePin,
    TogglePause,
    DisplayMenu,
    SelectDisplay,
    ResetDisplay,
    Settings,
    CheckForUpdates,
    OpenLogs,
    ToggleSafeMode,
    Restart,
    Exit,
    Separator,
}

/// <param name="Argument">Command data (monitor id for <see cref="TrayCommand.SelectDisplay"/>); never shown or logged.</param>
/// <param name="AutomationId">Stable id for UI Automation and tests.</param>
public sealed record TrayMenuItem(
    TrayCommand Command,
    string Label,
    bool Enabled = true,
    bool? Checked = null,
    string? ToolTip = null,
    string? Argument = null,
    string? AutomationId = null,
    IReadOnlyList<TrayMenuItem>? Children = null)
{
    public static TrayMenuItem Separator { get; } = new(TrayCommand.Separator, string.Empty);
}

/// <summary>
/// Pure tray menu model (guide §0.1 tray list, P03-02): labels and enabled/checked states for the current island state,
/// Safe Mode and attached displays. Access keys use '_' (WPF). Monitor ids travel as arguments, never as labels.
/// </summary>
public static class TrayMenuModel
{
    public static IReadOnlyList<TrayMenuItem> Build(IslandState state, bool safeMode, IReadOnlyList<MonitorInfo> monitors, string? islandMonitorId)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        var hidden = state is IslandState.Hidden or IslandState.Paused;
        var paused = state == IslandState.Paused;
        var pinnable = state is IslandState.Collapsed or IslandState.Peek or IslandState.Expanded or IslandState.Pinned;

        return
        [
            new(TrayCommand.ToggleVisibility, hidden ? "_Show island" : "_Hide island", Enabled: !paused, AutomationId: "Tray.ToggleVisibility"),
            new(TrayCommand.TogglePin, "_Pin island", Enabled: pinnable, Checked: state == IslandState.Pinned, AutomationId: "Tray.TogglePin"),
            new(TrayCommand.TogglePause, paused ? "_Resume" : "P_ause", ToolTip: paused ? null : "Hide the island and pause its background work", AutomationId: "Tray.TogglePause"),
            new(TrayCommand.DisplayMenu, "_Display", AutomationId: "Tray.Display", Children: DisplayItems(monitors, islandMonitorId)),
            TrayMenuItem.Separator,
            new(TrayCommand.Settings, "_Settings…", AutomationId: "Tray.Settings"),
            new(TrayCommand.CheckForUpdates, "Check for _updates", Enabled: false, ToolTip: "Update checks arrive in a later version", AutomationId: "Tray.CheckForUpdates"),
            new(TrayCommand.OpenLogs, "Open _logs folder", AutomationId: "Tray.OpenLogs"),
            TrayMenuItem.Separator,
            new(TrayCommand.ToggleSafeMode, safeMode ? "_Leave Safe Mode (restart)" : "Restart in Safe _Mode", AutomationId: "Tray.ToggleSafeMode"),
            new(TrayCommand.Restart, "_Restart", AutomationId: "Tray.Restart"),
            new(TrayCommand.Exit, "E_xit", AutomationId: "Tray.Exit"),
        ];
    }

    /// <summary>The order behind the user-visible "Display N" numbers (tray and settings): left to right, then top to bottom.</summary>
    public static MonitorInfo[] OrderDisplays(IReadOnlyList<MonitorInfo> monitors) =>
        [.. monitors.OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top)];

    /// <summary>Displays ordered left-to-right, top-to-bottom: "Display 1 (primary)", "Display 2", …, then reset.</summary>
    private static TrayMenuItem[] DisplayItems(IReadOnlyList<MonitorInfo> monitors, string? islandMonitorId)
    {
        var ordered = OrderDisplays(monitors);
        var items = new List<TrayMenuItem>(ordered.Length + 2);
        for (var i = 0; i < ordered.Length; i++)
        {
            var monitor = ordered[i];
            var label = $"Display _{i + 1}" + (monitor.IsPrimary ? " (primary)" : string.Empty);
            var isCurrent = string.Equals(monitor.Id, islandMonitorId, StringComparison.OrdinalIgnoreCase);
            items.Add(new TrayMenuItem(TrayCommand.SelectDisplay, label, Checked: isCurrent, Argument: monitor.Id, AutomationId: $"Tray.Display.{i + 1}"));
        }

        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem(TrayCommand.ResetDisplay, "_Reset to primary display", ToolTip: "Use if the island is on the wrong or a missing screen", AutomationId: "Tray.ResetDisplay"));
        return [.. items];
    }
}
