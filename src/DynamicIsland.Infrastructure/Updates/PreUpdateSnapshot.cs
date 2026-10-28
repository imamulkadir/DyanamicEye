using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DynamicIsland.Core.Distribution;
using DynamicIsland.Infrastructure.PersonalData;
using DynamicIsland.Infrastructure.Settings;

namespace DynamicIsland.Infrastructure.Updates;

/// <summary>What happened to a pre-update snapshot (ADR 0036).</summary>
public enum SnapshotState
{
    /// <summary>Taken by the updater; the swap has not finished.</summary>
    Taken,

    /// <summary>The new version confirmed its health; kept only as a safety copy.</summary>
    Committed,

    /// <summary>The update was rolled back; the old version checks its data against this copy at its next start.</summary>
    RolledBack,

    /// <summary>The old version restored this copy because the data had been upgraded beyond what it can read.</summary>
    Restored,

    /// <summary>The old version could use the data as it was; nothing was restored.</summary>
    Kept,

    /// <summary>A restore was needed but this copy failed validation; the data stays read-only for the old version.</summary>
    Invalid,
}

/// <summary>
/// Copy of the user's data taken by the updater after the app exited and before the swap (guide P06-07, ADR 0036):
/// <c>data\pre-update-&lt;UTC&gt;\</c> with the settings and database files and a <c>snapshot.json</c> marker. File copies
/// only (the app is closed, so the database and its WAL are consistent together); validation happens in the app.
/// </summary>
public static class PreUpdateSnapshot
{
    public const string FolderPrefix = "pre-update-";
    public const string MarkerFile = "snapshot.json";
    public const int Keep = 2;

    private const int MaxMarkerBytes = 4096;

    /// <summary>The data files copied, when present. The SQLite shared-memory file is never needed.</summary>
    public static readonly string[] Files =
    [
        JsonSettingsStore.FileName, JsonSettingsStore.BackupFileName,
        SqlitePersonalDataStore.FileName, SqlitePersonalDataStore.FileName + "-wal",
    ];

    /// <returns>The snapshot folder.</returns>
    /// <exception cref="IOException">The copy failed; the partial folder is removed.</exception>
    public static string Take(string dataFolder, string fromVersion, string toVersion, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolder);
        var folder = Path.Combine(dataFolder, FolderPrefix + now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        var partial = folder + ".partial";
        try
        {
            DeleteDirectory(partial);
            Directory.CreateDirectory(partial);
            foreach (var name in Files)
            {
                var source = Path.Combine(dataFolder, name);
                if (File.Exists(source))
                {
                    File.Copy(source, Path.Combine(partial, name));
                }
            }

            WriteMarker(partial, fromVersion, toVersion, SnapshotState.Taken);
            DeleteDirectory(folder);
            Directory.Move(partial, folder);
        }
        catch (UnauthorizedAccessException ex)
        {
            DeleteDirectory(partial);
            throw new IOException("The pre-update snapshot could not be written.", ex);
        }
        catch (IOException)
        {
            DeleteDirectory(partial);
            throw;
        }

        foreach (var old in Directory.GetDirectories(dataFolder, FolderPrefix + "*")
                     .Where(d => !d.EndsWith(".partial", StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(d => d, StringComparer.Ordinal)
                     .Skip(Keep))
        {
            DeleteDirectory(old);
        }

        return folder;
    }

    public static void Mark(string folder, SnapshotState state)
    {
        if (Read(folder) is { } marker)
        {
            WriteMarker(folder, marker.From, marker.To, state);
        }
    }

    /// <returns>The newest snapshot whose update was rolled back and not yet checked by the old version, or <c>null</c>.</returns>
    public static (string Folder, string From, string To)? FindRolledBack(string dataFolder)
    {
        if (!Directory.Exists(dataFolder))
        {
            return null;
        }

        foreach (var folder in Directory.GetDirectories(dataFolder, FolderPrefix + "*").OrderByDescending(d => d, StringComparer.Ordinal))
        {
            if (Read(folder) is { State: SnapshotState.RolledBack } marker)
            {
                return (folder, marker.From, marker.To);
            }
        }

        return null;
    }

    public static (string From, string To, SnapshotState State)? Read(string folder)
    {
        try
        {
            var file = new FileInfo(Path.Combine(folder, MarkerFile));
            if (file is not { Exists: true, Length: <= MaxMarkerBytes } || JsonNode.Parse(File.ReadAllText(file.FullName)) is not JsonObject json)
            {
                return null;
            }

            return Text(json, "from") is { } from && AppLayout.IsValidVersion(from)
                   && Text(json, "to") is { } to && AppLayout.IsValidVersion(to)
                   && Enum.TryParse<SnapshotState>(Text(json, "state"), ignoreCase: false, out var state) && Enum.IsDefined(state)
                ? (from, to, state)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonObject json, string name) =>
        json[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static void WriteMarker(string folder, string from, string to, SnapshotState state)
    {
        var path = Path.Combine(folder, MarkerFile);
        var temp = path + ".tmp";
        File.WriteAllText(temp, new JsonObject { ["from"] = from, ["to"] = to, ["state"] = state.ToString() }.ToJsonString());
        File.Move(temp, path, overwrite: true);
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next update's cleanup.
        }
    }
}
