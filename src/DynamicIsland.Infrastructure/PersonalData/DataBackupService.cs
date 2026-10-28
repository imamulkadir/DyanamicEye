using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using DynamicIsland.Core.PersonalData;
using DynamicIsland.Core.Settings;
using DynamicIsland.Infrastructure.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.Infrastructure.PersonalData;

/// <summary>
/// Backup archives of notes, tasks and settings (ADR 0024): consistent export with the SQLite online backup API,
/// a complete dry-run validation before restore, and an atomic restore applied at the next start with a rollback copy.
/// Logs carry event names, counts and sizes only.
/// </summary>
public sealed partial class DataBackupService(
    string dataDirectory,
    SqlitePersonalDataStore store,
    string appVersion,
    TimeProvider time,
    ILogger<DataBackupService> logger) : IDataBackupService
{
    public const string Format = "dynamicisland-backup";
    public const int FormatVersion = 1;
    public const string ManifestEntry = "manifest.json";
    public const string PendingFolderName = "restore-pending";
    public const string StagingFolderName = "restore-staging";
    public const string RollbackPrefix = "rollback-";
    public const int KeepRollbacks = 3;
    public const long MaxEntryBytes = 64L * 1024 * 1024;
    public const long MaxTotalBytes = 256L * 1024 * 1024;

    private const long MaxManifestBytes = 64 * 1024;
    private static readonly string[] DataEntries = [JsonSettingsStore.FileName, SqlitePersonalDataStore.FileName];
    private static readonly string[] LiveFiles =
    [
        JsonSettingsStore.FileName, JsonSettingsStore.BackupFileName,
        SqlitePersonalDataStore.FileName, SqlitePersonalDataStore.FileName + "-wal", SqlitePersonalDataStore.FileName + "-shm",
    ];

    private static readonly JsonSerializerOptions ManifestJson = new() { WriteIndented = true };

    public async Task<BackupExportResult> ExportAsync(string destinationPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (File.Exists(destinationPath))
        {
            throw new IOException("The destination already exists.");
        }

        // Counting also creates the database on first use, so there is always something to copy.
        var notes = await store.CountNotesAsync(cancellationToken).ConfigureAwait(false);
        var (open, done) = await store.CountTasksAsync(cancellationToken).ConfigureAwait(false);

        return await Task.Run(() =>
        {
            var work = Directory.CreateTempSubdirectory("DynamicIsland-backup-");
            var temporaryArchive = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var files = new List<(string Name, string Path)>();
                var databaseCopy = Path.Combine(work.FullName, SqlitePersonalDataStore.FileName);
                SnapshotDatabase(store.DatabasePath, databaseCopy);
                files.Add((SqlitePersonalDataStore.FileName, databaseCopy));

                var settingsPath = Path.Combine(dataDirectory, JsonSettingsStore.FileName);
                if (File.Exists(settingsPath))
                {
                    var settingsCopy = Path.Combine(work.FullName, JsonSettingsStore.FileName);
                    using (var source = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var target = File.Create(settingsCopy))
                    {
                        source.CopyTo(target);
                    }

                    files.Add((JsonSettingsStore.FileName, settingsCopy));
                }

                var manifest = new JsonObject
                {
                    ["format"] = Format,
                    ["formatVersion"] = FormatVersion,
                    ["createdUtc"] = time.GetUtcNow().ToString("o", CultureInfo.InvariantCulture),
                    ["appVersion"] = appVersion,
                    ["settingsSchema"] = AppSettings.CurrentSchemaVersion,
                    ["dataSchema"] = SqlitePersonalDataStore.CurrentSchemaVersion,
                    ["notes"] = notes,
                    ["tasks"] = open + done,
                    ["files"] = new JsonArray([.. files.Select(f => (JsonNode)new JsonObject
                    {
                        ["name"] = f.Name,
                        ["bytes"] = new FileInfo(f.Path).Length,
                        ["sha256"] = Hash(f.Path),
                    })]),
                };

                using (var archiveStream = new FileStream(temporaryArchive, FileMode.CreateNew, FileAccess.Write))
                using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create))
                {
                    var manifestEntry = archive.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
                    using (var writer = manifestEntry.Open())
                    {
                        JsonSerializer.Serialize(writer, manifest, ManifestJson);
                    }

                    foreach (var (name, path) in files)
                    {
                        archive.CreateEntryFromFile(path, name, CompressionLevel.Optimal);
                    }
                }

                File.Move(temporaryArchive, destinationPath, overwrite: false);
                var bytes = new FileInfo(destinationPath).Length;
                BackupLog.Exported(logger, notes, open + done, bytes);
                return new BackupExportResult(notes, open + done, bytes);
            }
            finally
            {
                TryDeleteFile(temporaryArchive);
                TryDeleteDirectory(work.FullName);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<BackupPreview> PreviewAsync(string archivePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        return Task.Run(() =>
        {
            var stagingRoot = Path.Combine(dataDirectory, StagingFolderName);
            TryDeleteDirectory(stagingRoot); // Older, abandoned previews.
            var stagingId = Guid.NewGuid().ToString("N");
            var staging = Directory.CreateDirectory(Path.Combine(stagingRoot, stagingId)).FullName;

            var preview = Validate(archivePath, staging, stagingId, cancellationToken);
            if (!preview.CanRestore)
            {
                TryDeleteDirectory(staging);
            }

            BackupLog.Previewed(logger, preview.Problem);
            return preview;
        }, cancellationToken);
    }

    public Task<bool> ScheduleRestoreAsync(BackupPreview approved, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approved);
        if (!approved.CanRestore)
        {
            return Task.FromResult(false);
        }

        return Task.Run(() =>
        {
            var staging = Path.Combine(dataDirectory, StagingFolderName, approved.StagingId!);
            if (!Directory.Exists(staging))
            {
                return false;
            }

            var pending = Path.Combine(dataDirectory, PendingFolderName);
            TryDeleteDirectory(pending);
            Directory.Move(staging, pending);
            BackupLog.Scheduled(logger);
            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Applies a scheduled restore. Call at startup <b>before</b> settings or the database are opened. Current files move
    /// to <c>rollback-&lt;UTC&gt;</c>; on any failure they are moved back and the pending folder is kept.
    /// </summary>
    /// <returns><c>null</c> when nothing was pending; otherwise whether the restore was applied.</returns>
    public static bool? ApplyPendingRestore(string dataDirectory, TimeProvider time, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        var pending = Path.Combine(dataDirectory, PendingFolderName);
        if (!File.Exists(Path.Combine(pending, SqlitePersonalDataStore.FileName)))
        {
            return null;
        }

        var restoresSettings = File.Exists(Path.Combine(pending, JsonSettingsStore.FileName));
        var rollback = Path.Combine(dataDirectory, RollbackPrefix + time.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        var moved = new List<string>();
        var restored = new List<string>();
        try
        {
            Directory.CreateDirectory(rollback);
            foreach (var name in LiveFiles.Where(n => restoresSettings || !n.StartsWith("settings.json", StringComparison.Ordinal)))
            {
                var live = Path.Combine(dataDirectory, name);
                if (File.Exists(live))
                {
                    File.Move(live, Path.Combine(rollback, name));
                    moved.Add(name);
                }
            }

            foreach (var name in DataEntries)
            {
                var incoming = Path.Combine(pending, name);
                if (File.Exists(incoming))
                {
                    File.Move(incoming, Path.Combine(dataDirectory, name));
                    restored.Add(name);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Put the previous data back exactly as it was; keep the pending folder for diagnosis.
            foreach (var name in restored)
            {
                File.Move(Path.Combine(dataDirectory, name), Path.Combine(pending, name), overwrite: true);
            }

            foreach (var name in moved)
            {
                File.Move(Path.Combine(rollback, name), Path.Combine(dataDirectory, name), overwrite: true);
            }

            BackupLog.RestoreFailed(logger, ex.GetType().Name);
            return false;
        }

        TryDeleteDirectory(pending);
        foreach (var old in Directory.GetDirectories(dataDirectory, RollbackPrefix + "*").OrderByDescending(d => d, StringComparer.Ordinal).Skip(KeepRollbacks))
        {
            TryDeleteDirectory(old);
        }

        BackupLog.Restored(logger, moved.Count);
        return true;
    }

    /// <summary>
    /// Online backup of the live database. The backup step only waits for a lock when the connections have a busy
    /// handler (a checkpoint or a writer may hold one briefly), so both get a timeout, plus a few retries.
    /// </summary>
    internal static void SnapshotDatabase(string source, string destination)
    {
        const int SqliteBusy = 5;
        const int SqliteLocked = 6;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var from = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
                using var to = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
                from.Open();
                to.Open();
                Scalar(from, "PRAGMA busy_timeout = 5000;");
                Scalar(to, "PRAGMA busy_timeout = 5000;");
                from.BackupDatabase(to);
                return;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteBusy or SqliteLocked && attempt < 5)
            {
                Thread.Sleep(200 * attempt);
            }
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static BackupPreview Validate(string archivePath, string staging, string stagingId, CancellationToken cancellationToken)
    {
        ZipArchive archive;
        FileStream? file = null;
        try
        {
            file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            archive = new ZipArchive(file, ZipArchiveMode.Read);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            file?.Dispose(); // Never leave the user's file locked.
            return BackupPreview.Refused(BackupProblem.NotAnArchive);
        }

        using (file)
        using (archive)
        {
            var names = archive.Entries.Select(e => e.FullName).ToArray();
            if (names.Length != names.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                || names.Any(n => n != ManifestEntry && !DataEntries.Contains(n))
                || !names.Contains(SqlitePersonalDataStore.FileName))
            {
                return BackupPreview.Refused(names.Contains(ManifestEntry) ? BackupProblem.UnexpectedEntries : BackupProblem.ManifestMissingOrInvalid);
            }

            if (archive.Entries.Any(e => e.Length > MaxEntryBytes) || archive.Entries.Sum(e => e.Length) > MaxTotalBytes)
            {
                return BackupPreview.Refused(BackupProblem.TooLarge);
            }

            if (archive.GetEntry(ManifestEntry) is not { } manifestEntry || ReadManifest(manifestEntry) is not { } manifest)
            {
                return BackupPreview.Refused(BackupProblem.ManifestMissingOrInvalid);
            }

            var listed = manifest.Files;
            if (listed.Count != names.Length - 1 || listed.Any(f => !DataEntries.Contains(f.Name) || archive.GetEntry(f.Name) is null))
            {
                return BackupPreview.Refused(BackupProblem.UnexpectedEntries);
            }

            foreach (var (name, bytes, sha256) in listed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.Combine(staging, name);
                var actual = Extract(archive.GetEntry(name)!, target);
                if (actual is null)
                {
                    return BackupPreview.Refused(BackupProblem.TooLarge);
                }

                if (actual.Value.Bytes != bytes || !string.Equals(actual.Value.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return BackupPreview.Refused(BackupProblem.ChecksumMismatch);
                }
            }

            var settingsPath = Path.Combine(staging, JsonSettingsStore.FileName);
            var hasSettings = File.Exists(settingsPath);
            if (hasSettings)
            {
                switch (SettingsSchema(settingsPath))
                {
                    case null:
                        return BackupPreview.Refused(BackupProblem.DamagedContent);
                    case > AppSettings.CurrentSchemaVersion:
                        return BackupPreview.Refused(BackupProblem.NewerVersion);
                }
            }

            var database = InspectDatabase(Path.Combine(staging, SqlitePersonalDataStore.FileName));
            if (database is null)
            {
                return BackupPreview.Refused(BackupProblem.DamagedContent);
            }

            if (database.Value.Schema > SqlitePersonalDataStore.CurrentSchemaVersion)
            {
                return BackupPreview.Refused(BackupProblem.NewerVersion);
            }

            return new BackupPreview(BackupProblem.None, manifest.CreatedUtc, manifest.AppVersion, database.Value.Notes, database.Value.Tasks, hasSettings, stagingId);
        }
    }

    private static Manifest? ReadManifest(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxManifestBytes)
        {
            return null;
        }

        try
        {
            using var stream = entry.Open();
            var node = JsonNode.Parse(stream) as JsonObject;
            if (node?["format"]?.GetValue<string>() != Format || node["formatVersion"]?.GetValue<int>() != FormatVersion || node["files"] is not JsonArray files)
            {
                return null;
            }

            var list = files.Select(f => (
                Name: f!["name"]!.GetValue<string>(),
                Bytes: f["bytes"]!.GetValue<long>(),
                Sha256: f["sha256"]!.GetValue<string>())).ToList();
            DateTimeOffset? created = DateTimeOffset.TryParse(node["createdUtc"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var c) ? c : null;
            return new Manifest(list, created, node["appVersion"]?.GetValue<string>());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or NullReferenceException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Copies an entry while counting, so a lying header cannot unpack more than <see cref="MaxEntryBytes"/>.</summary>
    private static (long Bytes, string Sha256)? Extract(ZipArchiveEntry entry, string target)
    {
        using var input = entry.Open();
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxEntryBytes)
            {
                return null;
            }

            output.Write(buffer, 0, read);
            hash.AppendData(buffer, 0, read);
        }

        return (total, Convert.ToHexString(hash.GetHashAndReset()));
    }

    internal static int? SettingsSchema(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("schemaVersion", out var version)
                   && version.TryGetInt32(out var schema)
                ? schema
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static (int Schema, int Notes, int Tasks)? InspectDatabase(string path)
    {
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();
            if (!string.Equals(Scalar(connection, "PRAGMA quick_check;") as string, "ok", StringComparison.Ordinal))
            {
                return null;
            }

            var schema = Convert.ToInt32(Scalar(connection, "PRAGMA user_version;"), CultureInfo.InvariantCulture);
            if (schema < 1 || schema > SqlitePersonalDataStore.CurrentSchemaVersion)
            {
                return (schema, 0, 0);
            }

            return (schema,
                Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM notes;"), CultureInfo.InvariantCulture),
                Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM tasks;"), CultureInfo.InvariantCulture));
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort; a leftover temporary file is harmless.
        }
    }

    private static void TryDeleteDirectory(string path)
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
            // Best effort.
        }
    }

    private sealed record Manifest(List<(string Name, long Bytes, string Sha256)> Files, DateTimeOffset? CreatedUtc, string? AppVersion);

    private static partial class BackupLog
    {
        [LoggerMessage(EventId = 3110, EventName = "BackupExported", Level = LogLevel.Information,
            Message = "Backup exported ({Notes} notes, {Tasks} tasks, {Bytes} bytes)")]
        public static partial void Exported(ILogger logger, int notes, int tasks, long bytes);

        [LoggerMessage(EventId = 3111, EventName = "BackupPreviewed", Level = LogLevel.Information,
            Message = "Backup checked before restore ({Problem})")]
        public static partial void Previewed(ILogger logger, BackupProblem problem);

        [LoggerMessage(EventId = 3112, EventName = "RestoreScheduled", Level = LogLevel.Information,
            Message = "Restore scheduled for the next start")]
        public static partial void Scheduled(ILogger logger);

        [LoggerMessage(EventId = 3113, EventName = "RestoreApplied", Level = LogLevel.Warning,
            Message = "Backup restored; {Files} previous files kept in a rollback folder")]
        public static partial void Restored(ILogger logger, int files);

        [LoggerMessage(EventId = 3114, EventName = "RestoreFailed", Level = LogLevel.Error,
            Message = "Restore could not be applied ({ErrorType}); the previous data was kept")]
        public static partial void RestoreFailed(ILogger logger, string errorType);
    }
}
