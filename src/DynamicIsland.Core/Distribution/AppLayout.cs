using System.Text.Json;
using System.Text.RegularExpressions;
using DynamicIsland.Core.Lifecycle;

namespace DynamicIsland.Core.Distribution;

/// <summary>Which payload versions the stable launcher should run (<c>current.json</c> in the layout root, ADR 0030).</summary>
/// <param name="Current">Version folder to run (<c>app-&lt;Current&gt;</c>).</param>
/// <param name="Previous">Last known good version, kept until an update is confirmed healthy; run when Current is missing.</param>
public sealed record LayoutPointer(string Current, string? Previous);

/// <summary>Where the app to run was found.</summary>
/// <param name="UsedPrevious">The current payload was missing, so the previous one runs.</param>
public sealed record ResolvedApp(string AppPath, string? Version, bool UsedPrevious);

/// <summary>
/// Release layout (ADR 0030): a stable launcher at the root and immutable, versioned app payloads beside it.
/// <code>
/// DynamicIsland\
///   DynamicIsland.exe            launcher (stable path: shortcuts, the startup entry, restarts)
///   current.json                 { "current": "1.3.0", "previous": "1.2.0" }
///   app-1.3.0\DynamicIsland.exe  the app and everything it needs, never changed after install
/// </code>
/// Without <c>current.json</c> (development and test builds) the app sits next to <c>DynamicIsland.Launcher.exe</c>.
/// </summary>
public static partial class AppLayout
{
    public const string PointerFileName = "current.json";
    public const string PayloadPrefix = "app-";
    public const string AppExecutable = "DynamicIsland.exe";

    /// <summary>The launcher's name at the layout root (the user-facing <c>DynamicIsland.exe</c>).</summary>
    public const string RootLauncher = "DynamicIsland.exe";

    /// <summary>A pointer file is tiny; anything larger is not ours.</summary>
    public const int MaxPointerBytes = 4096;

    /// <summary>SemVer 2.0 core with an optional prerelease tag; also keeps the folder name free of path characters.</summary>
    public static bool IsValidVersion(string? version) =>
        version is { Length: > 0 and <= 64 } && VersionPattern().IsMatch(version);

    /// <returns><c>null</c> for anything that is not a valid pointer (corrupt, hand-edited, wrong shape).</returns>
    public static LayoutPointer? ParsePointer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxPointerBytes)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("current", out var current)
                || current.ValueKind != JsonValueKind.String
                || !IsValidVersion(current.GetString()))
            {
                return null;
            }

            string? previous = null;
            if (root.TryGetProperty("previous", out var previousElement) && previousElement.ValueKind == JsonValueKind.String)
            {
                previous = IsValidVersion(previousElement.GetString()) ? previousElement.GetString() : null;
            }

            return new LayoutPointer(current.GetString()!, previous);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string SerializePointer(LayoutPointer layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!IsValidVersion(layout.Current) || (layout.Previous is not null && !IsValidVersion(layout.Previous)))
        {
            throw new ArgumentException("Versions must be SemVer.", nameof(layout));
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("current", layout.Current);
            if (layout.Previous is null)
            {
                writer.WriteNull("previous");
            }
            else
            {
                writer.WriteString("previous", layout.Previous);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static string PayloadFolder(string root, string version) => Path.Combine(root, PayloadPrefix + version);

    /// <summary>
    /// The app the launcher in <paramref name="root"/> should start. With a pointer: the current payload, else the
    /// previous one; a broken pointer is treated as missing. Without one: the app next to the launcher (development
    /// layout), never the launcher itself.
    /// </summary>
    /// <param name="pointerJson">Contents of <c>current.json</c>, or <c>null</c> when there is none.</param>
    /// <param name="launcherPath">The running launcher, so a root <c>DynamicIsland.exe</c> never starts itself.</param>
    /// <returns><c>null</c> when no app can be found.</returns>
    public static ResolvedApp? ResolveApp(string root, string? pointerJson, Func<string, bool> fileExists, string? launcherPath)
    {
        ArgumentNullException.ThrowIfNull(fileExists);

        if (ParsePointer(pointerJson) is { } pointer)
        {
            var current = Path.Combine(PayloadFolder(root, pointer.Current), AppExecutable);
            if (fileExists(current))
            {
                return new ResolvedApp(current, pointer.Current, UsedPrevious: false);
            }

            if (pointer.Previous is { } previousVersion)
            {
                var previous = Path.Combine(PayloadFolder(root, previousVersion), AppExecutable);
                if (fileExists(previous))
                {
                    return new ResolvedApp(previous, previousVersion, UsedPrevious: true);
                }
            }

            return null;
        }

        var sibling = Path.Combine(root, AppExecutable);
        return fileExists(sibling) && !SamePath(sibling, launcherPath) ? new ResolvedApp(sibling, null, UsedPrevious: false) : null;
    }

    /// <summary>
    /// The launcher the app should register for sign-in and use for restarts: the stable root launcher when the app runs
    /// from a payload folder of a release layout, else <c>DynamicIsland.Launcher.exe</c> next to the app.
    /// </summary>
    public static string LauncherFor(string appDirectory, Func<string, bool> fileExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);
        var folder = Path.TrimEndingDirectorySeparator(appDirectory);
        return IsReleasePayload(appDirectory, fileExists)
            ? Path.Combine(Path.GetDirectoryName(folder)!, RootLauncher)
            : Path.Combine(folder, LauncherContract.LauncherExecutable);
    }

    /// <summary>Whether <paramref name="appDirectory"/> is an <c>app-&lt;version&gt;</c> payload with the root launcher and pointer above it.</summary>
    public static bool IsReleasePayload(string appDirectory, Func<string, bool> fileExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);
        ArgumentNullException.ThrowIfNull(fileExists);

        var folder = Path.TrimEndingDirectorySeparator(appDirectory);
        var name = Path.GetFileName(folder);
        var parent = Path.GetDirectoryName(folder);
        return parent is not null
            && name.StartsWith(PayloadPrefix, StringComparison.OrdinalIgnoreCase)
            && IsValidVersion(name[PayloadPrefix.Length..])
            && fileExists(Path.Combine(parent, PointerFileName))
            && fileExists(Path.Combine(parent, RootLauncher));
    }

    private static bool SamePath(string path, string? other) =>
        other is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(other), StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
