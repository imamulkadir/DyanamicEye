using System.Globalization;
using System.Text.Json;

namespace DynamicIsland.Core.Updates;

/// <summary>
/// Turns a GitHub <c>releases/latest</c> response into a <see cref="ReleaseInfo"/>, trusting nothing (guide §5 trust
/// boundaries, ADR 0032): the tag must be SemVer, every URL must be HTTPS on github.com inside this repository, sizes
/// must be positive and bounded, and the notes stay plain text with a length limit.
/// </summary>
public static class GitHubReleaseParser
{
    public const int MaxResponseBytes = 1024 * 1024;
    public const int MaxNotesLength = 20_000;
    public const int MaxAssets = 50;
    public const long MaxAssetBytes = 1024L * 1024 * 1024;

    /// <returns><c>null</c> for anything that does not look exactly like a published release of <paramref name="repository"/>.</returns>
    public static ReleaseInfo? Parse(string json, string repository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        if (string.IsNullOrEmpty(json) || json.Length > MaxResponseBytes)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || Bool(root, "draft") || Bool(root, "prerelease") // Stable channel only (guide §7).
                || SemanticVersion.TryParse(String(root, "tag_name")) is not { } version
                || version.IsPrerelease
                || RepositoryUrl(String(root, "html_url"), repository, "/releases/tag/") is not { } page
                || !DateTimeOffset.TryParse(String(root, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var published))
            {
                return null;
            }

            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var assetList) && assetList.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetList.EnumerateArray().Take(MaxAssets))
                {
                    var name = String(asset, "name");
                    var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var bytes) ? bytes : 0;
                    var url = RepositoryUrl(String(asset, "browser_download_url"), repository, "/releases/download/");
                    if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || size <= 0 || size > MaxAssetBytes || url is null)
                    {
                        return null; // One bad asset makes the whole release untrusted.
                    }

                    assets.Add(new ReleaseAsset(name, size, url));
                }
            }

            var notes = (String(root, "body") ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
            if (notes.Length > MaxNotesLength)
            {
                notes = notes[..MaxNotesLength] + "\n…";
            }

            var title = String(root, "name") is { Length: > 0 and <= 200 } name2 ? name2 : $"Dynamic Island {version}";
            return new ReleaseInfo(version, String(root, "tag_name")!, title, published.ToUniversalTime(), page, notes, assets);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Only <c>https://github.com/&lt;repository&gt;&lt;pathPrefix&gt;…</c> is accepted.</summary>
    private static Uri? RepositoryUrl(string? text, string repository, string pathPrefix)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.AbsolutePath.StartsWith("/" + repository + pathPrefix, StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        return uri;
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
