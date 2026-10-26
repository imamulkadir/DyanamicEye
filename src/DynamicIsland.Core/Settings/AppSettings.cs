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

    public double Opacity { get; init; } = 1.0;

    public IslandPlacement Placement { get; init; } = IslandPlacement.TopCenter;

    /// <summary>Horizontal nudge in DIPs (right for top-left/center, inward for top-right).</summary>
    public double HorizontalOffset { get; init; }

    /// <summary>Gap below the top of the work area in DIPs; 0 attaches the notch to the top edge.</summary>
    public double VerticalOffset { get; init; }

    public MotionPreference Motion { get; init; } = MotionPreference.System;

    /// <summary>The clock shown in the notch (ADR 0016). Added without a schema bump (ADR 0003 §7: new property with a default).</summary>
    public ClockSettings Clock { get; init; } = new();
}

public sealed record ClockSettings
{
    public ClockHourFormat HourFormat { get; init; } = ClockHourFormat.System;

    public bool ShowSeconds { get; init; }

    public ClockDateFormat DateFormat { get; init; } = ClockDateFormat.None;
}

public sealed record BehaviorSettings
{
    public bool HoverPreviewEnabled { get; init; } = true;

    public int HoverPreviewDelayMs { get; init; } = 300;

    /// <summary>Auto-hide while a fullscreen app is in the foreground (D06).</summary>
    public bool AutoHideInFullscreen { get; init; } = true;

    /// <summary>
    /// Executable file names (e.g. <c>vlc.exe</c>) whose fullscreen windows do not hide the island (D06 manual override).
    /// Personal data: stored locally only, never logged or exported.
    /// </summary>
    public IReadOnlyList<string> FullscreenExceptions { get; init; } = [];

    /// <summary>Value equality including the exceptions list (records compare lists by reference otherwise).</summary>
    public bool Equals(BehaviorSettings? other) =>
        other is not null
        && HoverPreviewEnabled == other.HoverPreviewEnabled
        && HoverPreviewDelayMs == other.HoverPreviewDelayMs
        && AutoHideInFullscreen == other.AutoHideInFullscreen
        && (FullscreenExceptions ?? []).SequenceEqual(other.FullscreenExceptions ?? [], StringComparer.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(HoverPreviewEnabled, HoverPreviewDelayMs, AutoHideInFullscreen, FullscreenExceptions?.Count ?? 0);
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

public enum ClockHourFormat
{
    /// <summary>Follow the Windows time format.</summary>
    System,
    TwelveHour,
    TwentyFourHour,
}

/// <summary>Date shown before the time; texts follow the Windows region format.</summary>
public enum ClockDateFormat
{
    None,

    /// <summary>"Mon".</summary>
    Weekday,

    /// <summary>"Oct 26".</summary>
    Date,

    /// <summary>"Mon Oct 26".</summary>
    WeekdayAndDate,

    /// <summary>The Windows short date, e.g. "10/26/2026".</summary>
    Numeric,
}

public enum MotionPreference
{
    /// <summary>Follow the Windows "animation effects" setting.</summary>
    System,
    Full,
    Reduced,
    Off,
}
