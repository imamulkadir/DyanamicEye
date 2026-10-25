namespace DynamicIsland.Core.Settings;

/// <summary>
/// User preferences persisted as versioned JSON. Defaults here are the first-run values.
/// Holds no personal content (notes, tasks, window titles) by design.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Schema written by this build. Bump only together with a migration step.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public GeneralSettings General { get; init; } = new();

    public AppearanceSettings Appearance { get; init; } = new();

    public BehaviorSettings Behavior { get; init; } = new();

    public IReadOnlyList<WidgetSetting> Widgets { get; init; } = WidgetIds.All.Select(id => new WidgetSetting(id, true)).ToArray();

    public UpdateSettings Updates { get; init; } = new();
}

public sealed record GeneralSettings
{
    /// <summary>Per-user sign-in startup (D02). Asked on first launch; never enabled without consent.</summary>
    public ConsentState StartWithWindows { get; init; } = ConsentState.NotAsked;

    /// <summary>Monitor device identifier chosen by the user (D05); <c>null</c> means the primary monitor.</summary>
    public string? PreferredMonitorId { get; init; }
}

public sealed record AppearanceSettings
{
    public ThemeMode Theme { get; init; } = ThemeMode.System;

    public double Scale { get; init; } = 1.0;

    public double Opacity { get; init; } = 0.95;

    public IslandPlacement Placement { get; init; } = IslandPlacement.TopCenter;

    public MotionPreference Motion { get; init; } = MotionPreference.System;
}

public sealed record BehaviorSettings
{
    public bool HoverPreviewEnabled { get; init; } = true;

    public int HoverPreviewDelayMs { get; init; } = 300;

    /// <summary>Auto-hide while a fullscreen app is in the foreground (D06).</summary>
    public bool AutoHideInFullscreen { get; init; } = true;
}

public sealed record UpdateSettings
{
    /// <summary>Daily GitHub update check. Asked on first launch; download and install are separate approvals.</summary>
    public ConsentState AutomaticChecks { get; init; } = ConsentState.NotAsked;
}

/// <summary>Display order is list order (D09).</summary>
public sealed record WidgetSetting(string Id, bool Enabled);

public enum ConsentState
{
    NotAsked,
    Granted,
    Declined,
}

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

public enum IslandPlacement
{
    TopCenter,
    TopLeft,
    TopRight,
}

public enum MotionPreference
{
    /// <summary>Follow the Windows "animation effects" setting.</summary>
    System,
    Full,
    Reduced,
    Off,
}
