using DynamicIsland.Core.PersonalData;
using DynamicIsland.Infrastructure.PersonalData;
using DynamicIsland.Infrastructure.Settings;
using DynamicIsland.Infrastructure.Updates;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicIsland.UnitTests.PersonalData;

/// <summary>Data after a rolled-back update (P06-07, ADR 0036) on real SQLite files and snapshots in a temporary folder.</summary>
public sealed class PreUpdateRecoveryTests : IDisposable
{
    private const string FutureMigration = "ALTER TABLE tasks ADD COLUMN priority INTEGER NOT NULL DEFAULT 0;";
    private const string CurrentSettings = """{"schemaVersion":1}""";
    private const string FutureSettings = """{"schemaVersion":99}""";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "DynamicIsland.UnitTests", Guid.NewGuid().ToString("N"));
    private readonly TimeProvider _time = TimeProvider.System;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task Data_upgraded_by_a_failed_version_is_replaced_by_the_validated_snapshot_and_kept_aside()
    {
        await SeedAsync("before update");
        var snapshot = RolledBackSnapshot();
        using (var failedVersion = Store([.. SqlitePersonalDataStore.DefaultMigrations, FutureMigration]))
        {
            await failedVersion.AddNoteAsync("written by the failed version", Ct);
        }

        SqliteConnection.ClearAllPools();

        Assert.Equal(SnapshotState.Restored, Apply());

        using (var oldVersion = Store())
        {
            Assert.False(oldVersion.IsReadOnly);
            Assert.Equal("before update", Assert.Single(await oldVersion.GetNotesAsync(10, Ct)).Text);
        }

        SqliteConnection.ClearAllPools();
        var aside = Assert.Single(Directory.GetDirectories(_folder, DataBackupService.RollbackPrefix + "*"));
        Assert.Equal(2, DataBackupService.InspectDatabase(Path.Combine(aside, SqlitePersonalDataStore.FileName))?.Schema);
        Assert.Equal(SnapshotState.Restored, PreUpdateSnapshot.Read(snapshot)?.State);
        Assert.Null(Apply()); // Checked once.
    }

    [Fact]
    public async Task Compatible_data_is_kept_as_it_is()
    {
        await SeedAsync("before update");
        var snapshot = RolledBackSnapshot();
        using (var sameSchema = Store())
        {
            await sameSchema.AddNoteAsync("still compatible", Ct);
        }

        SqliteConnection.ClearAllPools();

        Assert.Equal(SnapshotState.Kept, Apply());
        using var store = Store();
        Assert.Equal(2, await store.CountNotesAsync(Ct));
        Assert.Empty(Directory.GetDirectories(_folder, DataBackupService.RollbackPrefix + "*"));
        Assert.Equal(SnapshotState.Kept, PreUpdateSnapshot.Read(snapshot)?.State);
    }

    [Fact]
    public async Task Newer_settings_alone_also_bring_back_the_snapshot()
    {
        await SeedAsync("before update");
        RolledBackSnapshot();
        File.WriteAllText(Path.Combine(_folder, JsonSettingsStore.FileName), FutureSettings);

        Assert.Equal(SnapshotState.Restored, Apply());
        Assert.Equal(CurrentSettings, File.ReadAllText(Path.Combine(_folder, JsonSettingsStore.FileName)));
    }

    [Fact]
    public async Task A_snapshot_that_fails_validation_is_never_restored()
    {
        await SeedAsync("before update");
        var snapshot = RolledBackSnapshot();
        File.WriteAllText(Path.Combine(snapshot, SqlitePersonalDataStore.FileName), "not a database");
        using (var failedVersion = Store([.. SqlitePersonalDataStore.DefaultMigrations, FutureMigration]))
        {
            await failedVersion.CountNotesAsync(Ct);
        }

        SqliteConnection.ClearAllPools();

        Assert.Equal(SnapshotState.Invalid, Apply());

        // The newer data stays exactly where it was, and this version only reads it.
        using var oldVersion = Store();
        Assert.Equal(1, await oldVersion.CountNotesAsync(Ct));
        Assert.True(oldVersion.IsReadOnly);
        Assert.Empty(Directory.GetDirectories(_folder, DataBackupService.RollbackPrefix + "*"));
        Assert.False(Directory.Exists(Path.Combine(_folder, DataBackupService.PendingFolderName)));
    }

    [Fact]
    public void Nothing_happens_without_a_rolled_back_update()
    {
        Assert.Null(Apply());
        Directory.CreateDirectory(_folder);
        var committed = PreUpdateSnapshot.Take(_folder, "1.0.0", "1.1.0", _time.GetUtcNow());
        PreUpdateSnapshot.Mark(committed, SnapshotState.Committed);

        Assert.Null(Apply());
    }

    [Fact]
    public void Snapshots_copy_the_data_files_and_keep_only_the_newest_two()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, JsonSettingsStore.FileName), CurrentSettings);
        File.WriteAllText(Path.Combine(_folder, SqlitePersonalDataStore.FileName), "db");
        File.WriteAllText(Path.Combine(_folder, SqlitePersonalDataStore.FileName + "-wal"), "wal");
        File.WriteAllText(Path.Combine(_folder, SqlitePersonalDataStore.FileName + "-shm"), "shm");
        var start = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

        var folders = Enumerable.Range(0, 3).Select(i => PreUpdateSnapshot.Take(_folder, "1.0.0", "1.1.0", start.AddMinutes(i))).ToArray();

        Assert.Equal(folders[1..], Directory.GetDirectories(_folder, PreUpdateSnapshot.FolderPrefix + "*").Order(StringComparer.Ordinal));
        Assert.Equal("wal", File.ReadAllText(Path.Combine(folders[2], SqlitePersonalDataStore.FileName + "-wal")));
        Assert.False(File.Exists(Path.Combine(folders[2], SqlitePersonalDataStore.FileName + "-shm")));
        Assert.Equal(("1.0.0", "1.1.0", SnapshotState.Taken), PreUpdateSnapshot.Read(folders[2]));
    }

    private SnapshotState? Apply() => PreUpdateRecovery.Apply(_folder, _time, NullLogger.Instance);

    private string RolledBackSnapshot()
    {
        var folder = PreUpdateSnapshot.Take(_folder, "1.0.0", "1.1.0", _time.GetUtcNow());
        PreUpdateSnapshot.Mark(folder, SnapshotState.RolledBack);
        return folder;
    }

    private async Task SeedAsync(string note)
    {
        using (var store = Store())
        {
            Assert.Equal(PersonalDataResult.Saved, await store.AddNoteAsync(note, Ct));
        }

        SqliteConnection.ClearAllPools();
        File.WriteAllText(Path.Combine(_folder, JsonSettingsStore.FileName), CurrentSettings);
    }

    private SqlitePersonalDataStore Store() => new(_folder, _time, NullLogger<SqlitePersonalDataStore>.Instance);

    private SqlitePersonalDataStore Store(string[] migrations) => new(_folder, _time, NullLogger<SqlitePersonalDataStore>.Instance, migrations);
}
