using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DynamicIsland.Core.Updates;

namespace DynamicIsland.Infrastructure.Updates;

/// <summary>
/// <c>data/update-check.json</c>: when GitHub was last asked, the ETag and the last answer (ADR 0032). Written
/// atomically; anything unreadable or untrusted loads as empty (the next check simply asks again). Contains only public
/// release metadata — nothing about the user.
/// </summary>
public sealed class JsonUpdateStateStore(string dataDirectory) : IUpdateStateStore
{
    public const string FileName = "update-check.json";
    private const int SchemaVersion = 1;
    private const int MaxFileBytes = 256 * 1024;

    private readonly string _path = Path.Combine(dataDirectory, FileName);

    public UpdateCheckState Load()
    {
        try
        {
            if (!File.Exists(_path) || new FileInfo(_path).Length > MaxFileBytes)
            {
                return UpdateCheckState.Empty;
            }

            if (JsonNode.Parse(File.ReadAllText(_path)) is not JsonObject root || (int?)root["schemaVersion"] != SchemaVersion)
            {
                return UpdateCheckState.Empty;
            }

            return new UpdateCheckState(
                Time(root["lastAttemptUtc"]),
                Time(root["lastSuccessUtc"]),
                Math.Clamp((int?)root["consecutiveFailures"] ?? 0, 0, 100),
                Time(root["retryAfterUtc"]),
                (string?)root["etag"],
                root["latest"] is JsonObject latest ? Release(latest) : null,
                (bool?)root["noReleases"] ?? false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            return UpdateCheckState.Empty;
        }
    }

    public void Save(UpdateCheckState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var document = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["lastAttemptUtc"] = Text(state.LastAttemptUtc),
            ["lastSuccessUtc"] = Text(state.LastSuccessUtc),
            ["consecutiveFailures"] = state.ConsecutiveFailures,
            ["retryAfterUtc"] = Text(state.RetryAfterUtc),
            ["etag"] = state.ETag,
            ["noReleases"] = state.NoReleases,
            ["latest"] = state.Latest is { } latest ? Node(latest) : null,
        };

        var tempPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(tempPath, document.ToJsonString());
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The answer stays in memory for this run; the next run asks again.
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort.
            }
        }
    }

    private static JsonObject Node(ReleaseInfo release) => new()
    {
        ["version"] = release.Version.ToString(),
        ["tag"] = release.Tag,
        ["title"] = release.Title,
        ["publishedUtc"] = Text(release.PublishedUtc),
        ["pageUrl"] = release.PageUrl.ToString(),
        ["notes"] = release.Notes,
        ["assets"] = new JsonArray(release.Assets.Select(a => (JsonNode)new JsonObject
        {
            ["name"] = a.Name,
            ["sizeBytes"] = a.SizeBytes,
            ["downloadUrl"] = a.DownloadUrl.ToString(),
        }).ToArray()),
    };

    /// <summary>Re-validated on load: a local file is as untrusted as the network (only github.com HTTPS links).</summary>
    private static ReleaseInfo? Release(JsonObject node)
    {
        if (SemanticVersion.TryParse((string?)node["version"]) is not { } version
            || Time(node["publishedUtc"]) is not { } published
            || GitHubUrl((string?)node["pageUrl"]) is not { } page)
        {
            return null;
        }

        var assets = new List<ReleaseAsset>();
        foreach (var asset in (node["assets"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var size = (long?)asset["sizeBytes"] ?? 0;
            if ((string?)asset["name"] is not { Length: > 0 } name || size <= 0 || GitHubUrl((string?)asset["downloadUrl"]) is not { } url)
            {
                return null;
            }

            assets.Add(new ReleaseAsset(name, size, url));
        }

        var notes = (string?)node["notes"] ?? string.Empty;
        return new ReleaseInfo(
            version,
            (string?)node["tag"] ?? "v" + version,
            (string?)node["title"] ?? $"Dynamic Island {version}",
            published,
            page,
            notes.Length > GitHubReleaseParser.MaxNotesLength ? notes[..GitHubReleaseParser.MaxNotesLength] : notes,
            assets);
    }

    private static Uri? GitHubUrl(string? text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ? uri : null;

    private static string? Text(DateTimeOffset? time) => time?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? Time(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time) ? time : null;
}
