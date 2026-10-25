namespace DynamicIsland.Core.Settings;

/// <summary>Stable identifiers of the built-in v1 widgets. Persisted; never rename an existing value.</summary>
public static class WidgetIds
{
    public const string Clock = "clock";
    public const string Media = "media";
    public const string SystemStats = "system";
    public const string Volume = "volume";
    public const string Notes = "notes";
    public const string Tasks = "tasks";

    /// <summary>Known widgets in default display order.</summary>
    public static IReadOnlyList<string> All { get; } = [Clock, Media, SystemStats, Volume, Notes, Tasks];

    public static bool IsKnown(string? id) => id is not null && All.Contains(id, StringComparer.Ordinal);
}
