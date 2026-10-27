using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using DynamicIsland.Core.Distribution;
using DynamicIsland.Core.Updates;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.Infrastructure.Updates;

/// <summary>
/// App side of the updater protocol (ADR 0035): writes the hand-over request for a staged, verified package, and on the
/// next start acknowledges the new version's health or picks up the updater's rollback note. Files only; starting the
/// updater and exiting are the caller's job.
/// </summary>
public sealed partial class UpdateHandover(string updatesFolder, ILogger logger)
{
    private const int MaxFailureBytes = 4096;

    private readonly string _updates = Path.GetFullPath(updatesFolder);

    public string RequestPath => Path.Combine(_updates, UpdateRequest.FileName);

    /// <summary>Checks the copy can be updated in place and writes <see cref="RequestPath"/>.</summary>
    /// <param name="appDirectory">The running payload folder (<c>app-&lt;version&gt;</c>).</param>
    /// <returns><c>null</c> when the request was written; otherwise a short reason code for the log.</returns>
    public string? Prepare(StagedPackage package, string appDirectory, int appProcessId, int? launcherProcessId)
    {
        ArgumentNullException.ThrowIfNull(package);

        // The release layout (ADR 0030): the launcher and current.json sit one folder above the payload.
        if (!AppLayout.IsReleasePayload(appDirectory, File.Exists))
        {
            return Refuse("not-release-layout");
        }

        var root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDirectory)))!;

        // Staged by the downloader at updates\<version>\<file>; the updater accepts nothing else.
        var packagePath = Path.GetFullPath(package.Path);
        var stagedFolder = Path.GetDirectoryName(packagePath)!;
        var version = Path.GetFileName(stagedFolder);
        if (!AppLayout.IsValidVersion(version)
            || !string.Equals(Path.GetDirectoryName(stagedFolder), _updates, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(package.Asset.Name, UpdatePackages.PortableAsset, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(packagePath))
        {
            return Refuse("not-staged");
        }

        if (!CanWrite(root))
        {
            return Refuse("layout-read-only");
        }

        try
        {
            string sha;
            long size;
            using (var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                size = stream.Length;
                sha = Convert.ToHexStringLower(SHA256.HashData(stream));
            }

            var request = new UpdateRequest(version, packagePath, size, sha, root, appProcessId, launcherProcessId);
            var temp = RequestPath + ".tmp";
            File.WriteAllText(temp, request.Serialize());
            File.Move(temp, RequestPath, overwrite: true);
            LogPrepared(logger, version);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Refuse("request-write-failed");
        }
    }

    /// <summary>
    /// Called once the app has started properly: confirms its health when the updater is waiting for the version of the
    /// running payload (<c>app-&lt;version&gt;</c>, the exact name the updater extracted).
    /// </summary>
    /// <returns><c>true</c> when an acknowledgment was written.</returns>
    public bool AcknowledgeHealth(string appDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);
        var folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(appDirectory));
        var runningVersion = folder.StartsWith(AppLayout.PayloadPrefix, StringComparison.OrdinalIgnoreCase) ? folder[AppLayout.PayloadPrefix.Length..] : string.Empty;
        var awaiting = Path.Combine(_updates, UpdateHandshake.AwaitingHealthFile);
        if (!AppLayout.IsValidVersion(runningVersion) || StringOf(ReadSmallJson(awaiting), "version") != runningVersion)
        {
            return false;
        }

        try
        {
            File.WriteAllText(Path.Combine(_updates, UpdateHandshake.AckFileName(runningVersion)), string.Empty);
            LogHealthAcknowledged(logger, runningVersion);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogHealthAckFailed(logger, ex.GetType().Name);
            return false;
        }
    }

    /// <summary>Reads and removes the updater's rollback note, so it is shown once.</summary>
    /// <returns>The version that failed to start, or <c>null</c>.</returns>
    public string? TakeFailedVersion()
    {
        var path = Path.Combine(_updates, UpdateHandshake.LastFailureFile);
        var note = ReadSmallJson(path);
        if (note is null)
        {
            return null;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // Shown next time instead of on every start.
        }

        var version = StringOf(note, "version");
        return version is not null && AppLayout.IsValidVersion(version) ? version : null;
    }

    private static JsonObject? ReadSmallJson(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file is { Exists: true, Length: <= MaxFailureBytes } ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? StringOf(JsonObject? json, string name) =>
        json?[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static bool CanWrite(string folder)
    {
        var probe = Path.Combine(folder, ".write-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string Refuse(string reason)
    {
        LogRefused(logger, reason);
        return reason;
    }

    [LoggerMessage(EventId = 3005, Level = LogLevel.Information, Message = "Update to {Version} handed over to the updater")]
    private static partial void LogPrepared(ILogger logger, string version);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Warning, Message = "In-app update not possible ({Reason})")]
    private static partial void LogRefused(ILogger logger, string reason);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Information, Message = "Health of {Version} acknowledged to the updater")]
    private static partial void LogHealthAcknowledged(ILogger logger, string version);

    [LoggerMessage(EventId = 3008, Level = LogLevel.Warning, Message = "Health acknowledgment could not be written ({ErrorType})")]
    private static partial void LogHealthAckFailed(ILogger logger, string errorType);
}
