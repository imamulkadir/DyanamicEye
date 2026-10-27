using System.Globalization;
using System.Text.RegularExpressions;

namespace DynamicIsland.Core.Updates;

/// <summary>
/// SemVer 2.0 version with precedence rules (§11): numeric core first; a prerelease ranks below its release; prerelease
/// identifiers compare numerically when numeric, otherwise ordinally; build metadata is ignored. Release tags may
/// start with <c>v</c> (guide §7: <c>vX.Y.Z</c>, <c>vX.Y.Z-beta.N</c>).
/// </summary>
public sealed partial record SemanticVersion(int Major, int Minor, int Patch, string? Prerelease) : IComparable<SemanticVersion>
{
    public bool IsPrerelease => Prerelease is not null;

    /// <returns><c>null</c> when <paramref name="text"/> is not SemVer (with an optional leading <c>v</c>).</returns>
    public static SemanticVersion? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 128)
        {
            return null;
        }

        var match = Pattern().Match(text.Trim());
        if (!match.Success
            || !int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return null;
        }

        var prerelease = match.Groups["pre"].Success ? match.Groups["pre"].Value : null;
        return new SemanticVersion(major, minor, patch, prerelease);
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0)
        {
            return core;
        }

        return (Prerelease, other.Prerelease) switch
        {
            (null, null) => 0,
            (null, _) => 1, // 1.0.0 > 1.0.0-beta
            (_, null) => -1,
            _ => ComparePrerelease(Prerelease!, other.Prerelease!),
        };
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";

    private static int ComparePrerelease(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aNumeric = int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out var aNumber);
            var bNumeric = int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out var bNumber);
            var result = (aNumeric, bNumeric) switch
            {
                (true, true) => aNumber.CompareTo(bNumber),
                (true, false) => -1, // Numeric identifiers rank below alphanumeric ones.
                (false, true) => 1,
                _ => string.CompareOrdinal(a[i], b[i]),
            };
            if (result != 0)
            {
                return result;
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    [GeneratedRegex(@"^v?(?<major>0|[1-9]\d{0,8})\.(?<minor>0|[1-9]\d{0,8})\.(?<patch>0|[1-9]\d{0,8})(-(?<pre>[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*))?(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
