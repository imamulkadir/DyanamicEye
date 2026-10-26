#if DYNAMICISLAND_TEST_HOOKS
using DynamicIsland.Infrastructure.PersonalData;
using Microsoft.Data.Sqlite;

namespace DynamicIsland.WindowsSmokeTests;

/// <summary>
/// The real app applies a scheduled restore at startup, before it opens the database (P04-07, ADR 0024). Debug only:
/// isolated data folder.
/// </summary>
[Trait("Category", "WindowsSmoke")]
[Collection(RealAppTests.Name)]
public sealed class RestoreSmokeTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "DynamicIsland.SmokeTests", Guid.NewGuid().ToString("N"));
    private readonly DateTime _testStarted = DateTime.Now;

    public void Dispose()
    {
        RealApp.KillStrayApps(_testStarted);
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Pending_restore_is_applied_at_startup_and_the_old_data_is_kept()
    {
        var data = Path.Combine(_dataRoot, "data");
        var pending = Path.Combine(data, DataBackupService.PendingFolderName);
        await CreateDatabaseAsync(Path.Combine(data, SqlitePersonalDataStore.FileName), "current note");
        await CreateDatabaseAsync(Path.Combine(pending, SqlitePersonalDataStore.FileName), "restored note");

        using var app = RealApp.Start(RealApp.AppExe, new Dictionary<string, string> { ["DYNAMICISLAND_TEST_DATA_ROOT"] = _dataRoot });
        try
        {
            await RealApp.WaitForMainWindowAsync(app);
            await RealApp.CloseAndAssertCleanExitAsync(app);
        }
        finally
        {
            RealApp.KillIfRunning(app);
        }

        Assert.Equal("restored note", await ReadNoteAsync(Path.Combine(data, SqlitePersonalDataStore.FileName)));
        var rollback = Assert.Single(Directory.GetDirectories(data, DataBackupService.RollbackPrefix + "*"));
        Assert.Equal("current note", await ReadNoteAsync(Path.Combine(rollback, SqlitePersonalDataStore.FileName)));
        Assert.False(Directory.Exists(pending));
    }

    private static async Task CreateDatabaseAsync(string path, string note)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE notes (id INTEGER PRIMARY KEY, text TEXT NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL);
            CREATE TABLE tasks (id INTEGER PRIMARY KEY, text TEXT NOT NULL, done INTEGER NOT NULL DEFAULT 0, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, completed_utc TEXT NULL);
            INSERT INTO notes(text, created_utc, updated_utc) VALUES ($text, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
            PRAGMA user_version = 1;
            """;
        command.Parameters.AddWithValue("$text", note);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<string?> ReadNoteAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT text FROM notes LIMIT 1;";
        return (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }
}
#endif
