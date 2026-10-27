using System.Globalization;

namespace DynamicIsland.Core.Updates;

/// <summary>Short, plain sentences for an update check result (Settings › Updates, ADR 0032).</summary>
public static class UpdateMessages
{
    public static string Describe(UpdateCheckResult result, SemanticVersion installed, TimeZoneInfo zone, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(culture);

        string When(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, zone).ToString("g", culture);
        var checkedText = result.CheckedUtc is { } at ? $" (checked {When(at)})" : string.Empty;

        return result.Outcome switch
        {
            UpdateCheckOutcome.UpdateAvailable when result.Latest is { } latest =>
                $"Version {latest.Version} is available (released {TimeZoneInfo.ConvertTime(latest.PublishedUtc, zone).ToString("d", culture)}); you have {installed}. " +
                "Get it from the project's GitHub releases page; downloading and installing from here arrive in a later version.",
            UpdateCheckOutcome.UpToDate => $"You have the latest version, {installed}{checkedText}.",
            UpdateCheckOutcome.NoReleases => $"No version has been published on GitHub yet{checkedText}.",
            UpdateCheckOutcome.DisabledByPolicy => "Updates are managed for this copy, so it does not check GitHub.",
            UpdateCheckOutcome.NotAllowed => "Daily checks are off. Use Check for updates to look now.",
            UpdateCheckOutcome.RateLimited => "GitHub asked us to wait a while before checking again. Try later.",
            UpdateCheckOutcome.Failed => "Couldn't reach GitHub. Check your internet connection and try again.",
            _ => $"You have version {installed}{checkedText}.",
        };
    }
}
