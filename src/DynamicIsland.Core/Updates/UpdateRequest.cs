using System.Text.Json;
using System.Text.Json.Nodes;
using DynamicIsland.Core.Distribution;

namespace DynamicIsland.Core.Updates;

/// <summary>
/// Hand-over from the app to the separate updater (ADR 0035): what to install and which processes must exit first.
/// Written by the app into the per-user updates folder; the updater re-validates everything before acting.
/// </summary>
public sealed record UpdateRequest(
    string Version,
    string PackagePath,
    long Size,
    string Sha256,
    string LayoutRoot,
    int AppProcessId,
    int? LauncherProcessId)
{
    public const int SchemaVersion = 1;
    public const string FileName = "update-request.json";
    public const int MaxBytes = 16 * 1024;

    public string Serialize() => new JsonObject
    {
        ["schemaVersion"] = SchemaVersion,
        ["version"] = Version,
        ["packagePath"] = PackagePath,
        ["size"] = Size,
        ["sha256"] = Sha256,
        ["layoutRoot"] = LayoutRoot,
        ["appProcessId"] = AppProcessId,
        ["launcherProcessId"] = LauncherProcessId,
    }.ToJsonString();

    /// <returns><c>null</c> when the text is not a valid request (wrong schema, bad version or hash, missing fields).</returns>
    public static UpdateRequest? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > MaxBytes)
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(json) is not JsonObject o
                || o["schemaVersion"]?.GetValue<int>() != SchemaVersion
                || o["version"]?.GetValue<string>() is not { } version || !AppLayout.IsValidVersion(version)
                || o["packagePath"]?.GetValue<string>() is not { Length: > 0 } package
                || o["sha256"]?.GetValue<string>() is not { Length: 64 } sha || !sha.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'))
                || o["layoutRoot"]?.GetValue<string>() is not { Length: > 0 } root
                || o["size"]?.GetValue<long>() is not ({ } size and > 0)
                || o["appProcessId"]?.GetValue<int>() is not ({ } appPid and > 0))
            {
                return null;
            }

            var launcherPid = o["launcherProcessId"] is JsonValue v && v.TryGetValue(out int lp) && lp > 0 ? lp : (int?)null;
            return new UpdateRequest(version, package, size, sha, root, appPid, launcherPid);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}

/// <summary>Files the app and updater use to agree on health and failures (ADR 0035), all in the per-user updates folder.</summary>
public static class UpdateHandshake
{
    /// <summary>Names the version that must acknowledge its health after an update.</summary>
    public const string AwaitingHealthFile = "awaiting-health.json";

    /// <summary>Written by the updater after a rollback: <c>{"version":"x","reason":"code"}</c>, shown once by the app.</summary>
    public const string LastFailureFile = "last-update-failed.json";

    public static string AckFileName(string version) => $"health-{version}.ack";
}
