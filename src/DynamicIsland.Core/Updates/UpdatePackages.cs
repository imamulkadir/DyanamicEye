using System.Globalization;

namespace DynamicIsland.Core.Updates;

/// <summary>The release files (guide §7 names) and which one an in-app update uses.</summary>
public static class UpdatePackages
{
    public const string SetupAsset = "DynamicIsland-Setup-win-x64.exe";
    public const string PortableAsset = "DynamicIsland-Portable-win-x64.zip";

    /// <summary>
    /// Installed and portable copies both update in place from the portable ZIP: the updater swaps in its payload and
    /// can roll back (ADR 0035, superseding ADR 0033's per-kind choice). The setup program is for first installs.
    /// </summary>
    /// <returns>The ZIP, or <c>null</c> when the release does not have it.</returns>
    public static ReleaseAsset? For(ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return release.Assets.FirstOrDefault(a => string.Equals(a.Name, PortableAsset, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>"512 bytes", "48.6 MB", "1.2 GB" (1024-based, like File Explorer).</summary>
    public static string FormatSize(long bytes, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        if (bytes < 1024)
        {
            return string.Format(culture, "{0} bytes", bytes);
        }

        string[] units = ["KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = -1;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Format(culture, "{0:0.#} {1}", value, units[unit]);
    }
}
