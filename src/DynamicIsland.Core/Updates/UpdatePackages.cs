using System.Globalization;

namespace DynamicIsland.Core.Updates;

/// <summary>How this copy was set up, which decides the release file it updates from (ADR 0033).</summary>
public enum InstallKind
{
    /// <summary>Per-user installer (ADR 0031): updates with the setup program.</summary>
    Installed,

    /// <summary>Portable ZIP (ADR 0030): updates with the ZIP.</summary>
    Portable,
}

/// <summary>The release files (guide §7 names) and which one fits this copy.</summary>
public static class UpdatePackages
{
    public const string SetupAsset = "DynamicIsland-Setup-win-x64.exe";
    public const string PortableAsset = "DynamicIsland-Portable-win-x64.zip";

    /// <summary>Inno Setup's uninstaller, present at the layout root only when the installer put the copy there.</summary>
    public const string UninstallerFileName = "unins000.exe";

    public static InstallKind Detect(string layoutRoot, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        return fileExists(Path.Combine(layoutRoot, UninstallerFileName)) ? InstallKind.Installed : InstallKind.Portable;
    }

    /// <returns>The file for <paramref name="kind"/>, or <c>null</c> when the release does not have it.</returns>
    public static ReleaseAsset? For(ReleaseInfo release, InstallKind kind)
    {
        ArgumentNullException.ThrowIfNull(release);
        var name = kind == InstallKind.Installed ? SetupAsset : PortableAsset;
        return release.Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
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
