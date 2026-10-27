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
            UpdateCheckOutcome.UpdateAvailable or UpdateCheckOutcome.NotDue when Newer(result, installed) is { } latest =>
                $"Version {latest.Version} is available (released {TimeZoneInfo.ConvertTime(latest.PublishedUtc, zone).ToString("d", culture)}); you have {installed}.",
            UpdateCheckOutcome.UpToDate => $"You have the latest version, {installed}{checkedText}.",
            UpdateCheckOutcome.NoReleases => $"No version has been published on GitHub yet{checkedText}.",
            UpdateCheckOutcome.DisabledByPolicy => "Updates are managed for this copy, so it does not check GitHub.",
            UpdateCheckOutcome.NotAllowed => "Daily checks are off. Use Check for updates to look now.",
            UpdateCheckOutcome.RateLimited => "GitHub asked us to wait a while before checking again. Try later.",
            UpdateCheckOutcome.Failed => "Couldn't reach GitHub. Check your internet connection and try again.",
            _ => $"You have version {installed}{checkedText}.",
        };
    }

    /// <summary>The known release newer than <paramref name="installed"/>, also from the cache (a "not due" result after a restart).</summary>
    public static ReleaseInfo? Newer(UpdateCheckResult? result, SemanticVersion installed)
    {
        ArgumentNullException.ThrowIfNull(installed);
        return result is { Outcome: UpdateCheckOutcome.UpdateAvailable or UpdateCheckOutcome.NotDue or UpdateCheckOutcome.RateLimited or UpdateCheckOutcome.Failed, Latest: { } latest }
            && latest.Version > installed ? latest : null;
    }

    /// <summary>The update window's summary line: installed version, release date and the download size from GitHub.</summary>
    public static string Summary(ReleaseInfo release, ReleaseAsset? package, SemanticVersion installed, TimeZoneInfo zone, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(culture);
        var released = TimeZoneInfo.ConvertTime(release.PublishedUtc, zone).ToString("D", culture);
        var size = package is null ? string.Empty : $" Download size: {UpdatePackages.FormatSize(package.SizeBytes, culture)}.";
        return $"You have version {installed}. Released {released}.{size}";
    }

    /// <summary>"12.3 MB of 48.6 MB (25%)".</summary>
    public static string Progress(long received, long total, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var percent = total > 0 ? (int)(Math.Clamp(received, 0, total) * 100 / total) : 0;
        return $"{UpdatePackages.FormatSize(received, culture)} of {UpdatePackages.FormatSize(total, culture)} ({percent.ToString(culture)}%)";
    }
}
