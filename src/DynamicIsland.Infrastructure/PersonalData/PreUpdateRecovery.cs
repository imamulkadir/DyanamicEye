using DynamicIsland.Core.Settings;
using DynamicIsland.Infrastructure.Settings;
using DynamicIsland.Infrastructure.Updates;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.Infrastructure.PersonalData;

/// <summary>
/// After a rolled-back update (guide P06-07, ADR 0036): the failed new version may have upgraded the settings or the
/// database beyond what this version can read. Then, and only after the pre-update snapshot passes the same checks as a
/// backup restore, the snapshot is restored through the pending-restore path (ADR 0024), which keeps the upgraded files in
/// <c>rollback-&lt;UTC&gt;</c>. Otherwise nothing is touched and the stores stay read-only for newer data, as before.
/// </summary>
public static partial class PreUpdateRecovery
{
    /// <summary>Call at startup after <see cref="DataBackupService.ApplyPendingRestore"/> and before settings or the database open.</summary>
    /// <returns>The resulting snapshot state, or <c>null</c> when no rolled-back update awaited a check.</returns>
    public static SnapshotState? Apply(string dataDirectory, TimeProvider time, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (PreUpdateSnapshot.FindRolledBack(dataDirectory) is not { } snapshot)
        {
            return null;
        }

        var state = Check(dataDirectory, snapshot.Folder, time, logger);
        try
        {
            PreUpdateSnapshot.Mark(snapshot.Folder, state);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Checked again next start; a restore already applied is not repeated because the data is then compatible.
        }

        RecoveryLog.Checked(logger, snapshot.To, state);
        return state;
    }

    private static SnapshotState Check(string dataDirectory, string folder, TimeProvider time, ILogger logger)
    {
        var liveSettings = Path.Combine(dataDirectory, JsonSettingsStore.FileName);
        var liveDatabase = Path.Combine(dataDirectory, SqlitePersonalDataStore.FileName);
        if (!IsNewer(liveSettings, liveDatabase))
        {
            return SnapshotState.Kept;
        }

        var pending = Path.Combine(dataDirectory, DataBackupService.PendingFolderName);
        if (Directory.Exists(pending))
        {
            return SnapshotState.Invalid; // A user restore that could not be applied is left alone for diagnosis.
        }

        var staging = Path.Combine(dataDirectory, DataBackupService.StagingFolderName, "pre-update");
        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            Directory.CreateDirectory(staging);
            if (!Stage(folder, staging))
            {
                Directory.Delete(staging, recursive: true);
                return SnapshotState.Invalid;
            }

            Directory.Move(staging, pending);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            RecoveryLog.StageFailed(logger, ex.GetType().Name);
            return SnapshotState.Invalid;
        }

        return DataBackupService.ApplyPendingRestore(dataDirectory, time, logger) == true ? SnapshotState.Restored : SnapshotState.Invalid;
    }

    /// <summary>Whether either file has a schema newer than this version supports (unreadable files are left to the stores).</summary>
    private static bool IsNewer(string settings, string database)
    {
        try
        {
            return (File.Exists(settings) && DataBackupService.SettingsSchema(settings) > AppSettings.CurrentSchemaVersion)
                   || (File.Exists(database) && DataBackupService.InspectDatabase(database)?.Schema > SqlitePersonalDataStore.CurrentSchemaVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Copies the snapshot into <paramref name="staging"/> and validates it like a backup restore (ADR 0024).</summary>
    private static bool Stage(string folder, string staging)
    {
        var database = Path.Combine(folder, SqlitePersonalDataStore.FileName);
        if (!File.Exists(database))
        {
            return false; // Nothing to go back to; the newer database stays read-only.
        }

        // The online backup folds the snapshot's WAL into one file and leaves the snapshot itself unchanged.
        var stagedDatabase = Path.Combine(staging, SqlitePersonalDataStore.FileName);
        DataBackupService.SnapshotDatabase(database, stagedDatabase);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (DataBackupService.InspectDatabase(stagedDatabase) is not { Schema: >= 1 and <= SqlitePersonalDataStore.CurrentSchemaVersion })
        {
            return false;
        }

        var settings = Path.Combine(folder, JsonSettingsStore.FileName);
        if (File.Exists(settings))
        {
            if (DataBackupService.SettingsSchema(settings) is not (>= 1 and <= AppSettings.CurrentSchemaVersion))
            {
                return false;
            }

            File.Copy(settings, Path.Combine(staging, JsonSettingsStore.FileName));
        }

        return true;
    }

    private static partial class RecoveryLog
    {
        [LoggerMessage(EventId = 3120, EventName = "PreUpdateDataChecked", Level = LogLevel.Information,
            Message = "Data checked after the rolled-back update to {Version}: {State}")]
        public static partial void Checked(ILogger logger, string version, SnapshotState state);

        [LoggerMessage(EventId = 3121, EventName = "PreUpdateRestoreStageFailed", Level = LogLevel.Warning,
            Message = "The pre-update snapshot could not be prepared for restore ({ErrorType})")]
        public static partial void StageFailed(ILogger logger, string errorType);
    }
}
