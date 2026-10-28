using DynamicIsland.Core.PersonalData;
using DynamicIsland.Infrastructure.PersonalData;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicIsland.UnitTests.PersonalData;

/// <summary>Notes and tasks storage (P04-06, ADR 0023) on a real SQLite file in a temporary folder.</summary>
public sealed class SqlitePersonalDataStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "DynamicIsland.UnitTests", Guid.NewGuid().ToString("N"));
    private readonly SteppingTime _time = new();

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
    public async Task Notes_are_added_edited_listed_newest_first_and_deleted()
    {
        using var store = Create();
        var changes = 0;
        store.Changed += (_, _) => changes++;

        Assert.Equal(PersonalDataResult.Saved, await store.AddNoteAsync("  first  ", Ct));
        Assert.Equal(PersonalDataResult.Saved, await store.AddNoteAsync("second\r\nline", Ct));
        var notes = await store.GetNotesAsync(10, Ct);

        Assert.Equal(["second\nline", "first"], notes.Select(n => n.Text));
        Assert.True(notes[0].CreatedUtc > notes[1].CreatedUtc);

        Assert.Equal(PersonalDataResult.Saved, await store.EditNoteAsync(notes[1].Id, "first, edited", Ct));
        var edited = (await store.GetNotesAsync(10, Ct))[0];
        Assert.Equal("first, edited", edited.Text);
        Assert.True(edited.UpdatedUtc > edited.CreatedUtc);

        Assert.Equal(PersonalDataResult.Saved, await store.DeleteNoteAsync(edited.Id, Ct));
        Assert.Equal(PersonalDataResult.NotFound, await store.DeleteNoteAsync(edited.Id, Ct));
        Assert.Equal(1, await store.CountNotesAsync(Ct));
        Assert.Equal(4, changes); // Only saved changes notify.
    }

    [Fact]
    public async Task Tasks_complete_reopen_and_list_open_first_then_recently_done()
    {
        using var store = Create();
        foreach (var text in new[] { "a", "b", "c" })
        {
            await store.AddTaskAsync(text, Ct);
        }

        var tasks = await store.GetTasksAsync(10, Ct);
        await store.SetTaskDoneAsync(tasks[0].Id, true, Ct); // a
        await store.SetTaskDoneAsync(tasks[2].Id, true, Ct); // c, completed later

        var listed = await store.GetTasksAsync(10, Ct);
        Assert.Equal(["b", "c", "a"], listed.Select(t => t.Text));
        Assert.NotNull(listed[1].CompletedUtc);
        Assert.Equal((1, 2), await store.CountTasksAsync(Ct));

        await store.SetTaskDoneAsync(tasks[0].Id, false, Ct);
        var reopened = (await store.GetTasksAsync(10, Ct)).Single(t => t.Text == "a");
        Assert.False(reopened.Done);
        Assert.Null(reopened.CompletedUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tab\tinside")]
    [InlineData("line\nbreak")]
    public async Task Invalid_task_text_changes_nothing(string text)
    {
        using var store = Create();

        Assert.Equal(PersonalDataResult.Invalid, await store.AddTaskAsync(text, Ct));
        Assert.Equal(PersonalDataResult.Invalid, await store.AddTaskAsync(new string('x', PersonalDataRules.MaxTaskLength + 1), Ct));
        Assert.Equal((0, 0), await store.CountTasksAsync(Ct));
    }

    [Fact]
    public async Task Note_rules_allow_line_breaks_but_not_other_control_characters()
    {
        Assert.Equal("a\nb", PersonalDataRules.NormalizeNote("a\r\nb"));
        Assert.Null(PersonalDataRules.NormalizeNote("bell\a"));
        Assert.Null(PersonalDataRules.NormalizeNote(new string('n', PersonalDataRules.MaxNoteLength + 1)));

        using var store = Create();
        Assert.Equal(PersonalDataResult.Invalid, await store.EditNoteAsync(1, " ", Ct));
    }

    [Fact]
    public async Task Limit_is_enforced_inside_the_transaction()
    {
        using (var store = Create())
        {
            await store.AddNoteAsync("seed", Ct); // Creates the schema.
        }

        // Fill to the limit directly, then the store must refuse one more.
        await using (var connection = new SqliteConnection($"Data Source={Path.Combine(_folder, SqlitePersonalDataStore.FileName)}"))
        {
            await connection.OpenAsync(Ct);
            await using var transaction = await connection.BeginTransactionAsync(Ct);
            var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO notes(text, created_utc, updated_utc) VALUES ('x', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');";
            for (var i = 1; i < PersonalDataRules.MaxNotes; i++)
            {
                await insert.ExecuteNonQueryAsync(Ct);
            }

            await transaction.CommitAsync(Ct);
        }

        using var full = Create();
        Assert.Equal(PersonalDataResult.LimitReached, await full.AddNoteAsync("one too many", Ct));
        Assert.Equal(PersonalDataRules.MaxNotes, await full.CountNotesAsync(Ct));
    }

    [Fact]
    public async Task Data_survives_a_restart()
    {
        using (var first = Create())
        {
            await first.AddTaskAsync("persist me", Ct);
        }

        SqliteConnection.ClearAllPools();
        using var second = Create();
        Assert.Equal("persist me", Assert.Single(await second.GetTasksAsync(5, Ct)).Text);
    }

    [Fact]
    public async Task Corrupt_file_is_moved_aside_and_replaced()
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(Path.Combine(_folder, SqlitePersonalDataStore.FileName), "this is not a database", Ct);

        using var store = Create();
        Assert.Equal(PersonalDataResult.Saved, await store.AddNoteAsync("fresh", Ct));

        var preserved = Assert.Single(Directory.GetFiles(_folder, "islands.corrupt-*.db"));
        Assert.Equal("this is not a database", await File.ReadAllTextAsync(preserved, Ct));
        Assert.Equal(1, await store.CountNotesAsync(Ct));
    }

    [Fact]
    public async Task Newer_schema_is_opened_read_only()
    {
        using (var store = Create())
        {
            await store.AddNoteAsync("from the future", Ct);
        }

        await using (var connection = new SqliteConnection($"Data Source={Path.Combine(_folder, SqlitePersonalDataStore.FileName)}"))
        {
            await connection.OpenAsync(Ct);
            var bump = connection.CreateCommand();
            bump.CommandText = "PRAGMA user_version = 9;";
            await bump.ExecuteNonQueryAsync(Ct);
        }

        SqliteConnection.ClearAllPools();
        using var older = Create();
        Assert.Equal("from the future", Assert.Single(await older.GetNotesAsync(5, Ct)).Text);
        Assert.True(older.IsReadOnly);
        Assert.Equal(PersonalDataResult.ReadOnly, await older.AddNoteAsync("must not be written", Ct));
        Assert.Equal(1, await older.CountNotesAsync(Ct));
    }

    [Fact]
    public async Task Update_shaped_migration_keeps_notes_and_tasks()
    {
        await SeedCurrentVersionAsync();

        // A later release adds a column and a table (rehearsal only: v2 does not exist in the product).
        using (var updated = Create([.. SqlitePersonalDataStore.DefaultMigrations, RehearsalV2]))
        {
            Assert.Equal(["second note", "first note"], (await updated.GetNotesAsync(10, Ct)).Select(n => n.Text));
            var tasks = await updated.GetTasksAsync(10, Ct);
            Assert.Equal(["open task", "done task"], tasks.Select(t => t.Text));
            Assert.NotNull(tasks[1].CompletedUtc);
            Assert.Equal(PersonalDataResult.Saved, await updated.AddNoteAsync("after update", Ct));
            Assert.False(updated.IsReadOnly);
        }

        SqliteConnection.ClearAllPools();
        Assert.Equal(2, await UserVersionAsync());
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM tasks WHERE priority <> 0;"));

        // Going back to this build after the update: readable, never overwritten.
        using var rolledBack = Create();
        Assert.Equal(3, await rolledBack.CountNotesAsync(Ct));
        Assert.True(rolledBack.IsReadOnly);
    }

    [Fact]
    public async Task Failed_update_migration_rolls_back_and_keeps_data()
    {
        await SeedCurrentVersionAsync();

        using (var broken = Create([.. SqlitePersonalDataStore.DefaultMigrations, "ALTER TABLE tasks ADD COLUMN priority INTEGER NOT NULL DEFAULT 0; INSERT INTO missing_table VALUES (1);"]))
        {
            // Recovered gracefully (ADR 0036): still readable, never written to while the upgrade is incomplete.
            Assert.Equal(2, await broken.CountNotesAsync(Ct));
            Assert.True(broken.IsReadOnly);
            Assert.Equal(PersonalDataResult.ReadOnly, await broken.AddNoteAsync("must not be written", Ct));
        }

        SqliteConnection.ClearAllPools();
        Assert.Equal(1, await UserVersionAsync());
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM pragma_table_info('tasks') WHERE name = 'priority';"));

        // The fixed release then migrates the untouched data.
        using var fixedRelease = Create([.. SqlitePersonalDataStore.DefaultMigrations, RehearsalV2]);
        Assert.Equal(2, await fixedRelease.CountNotesAsync(Ct));
        Assert.Equal((1, 1), await fixedRelease.CountTasksAsync(Ct));
    }

    [Fact]
    public async Task Bundled_sqlite_is_recent_enough()
    {
        using var store = Create();
        await store.CountNotesAsync(Ct);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(Ct);
        var version = new Version(connection.ServerVersion);

        Assert.True(version >= new Version(3, 45), $"SQLite {version}");
    }

    private const string RehearsalV2 = """
        ALTER TABLE tasks ADD COLUMN priority INTEGER NOT NULL DEFAULT 0;
        CREATE TABLE tags (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE);
        """;

    private SqlitePersonalDataStore Create() => new(_folder, _time, NullLogger<SqlitePersonalDataStore>.Instance);

    private SqlitePersonalDataStore Create(string[] migrations) => new(_folder, _time, NullLogger<SqlitePersonalDataStore>.Instance, migrations);

    /// <summary>Two notes and two tasks (one done) written by the current build.</summary>
    private async Task SeedCurrentVersionAsync()
    {
        using (var store = Create())
        {
            await store.AddNoteAsync("first note", Ct);
            await store.AddNoteAsync("second note", Ct);
            await store.AddTaskAsync("done task", Ct);
            await store.AddTaskAsync("open task", Ct);
            var done = (await store.GetTasksAsync(10, Ct)).Single(t => t.Text == "done task");
            await store.SetTaskDoneAsync(done.Id, true, Ct);
        }

        SqliteConnection.ClearAllPools();
    }

    private Task<long> UserVersionAsync() => ScalarAsync("PRAGMA user_version;");

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(_folder, SqlitePersonalDataStore.FileName)};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(Ct);
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    /// <summary>Each reading is one second later, so timestamps are distinct and ordered.</summary>
    private sealed class SteppingTime : TimeProvider
    {
        private long _seconds;

        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero).AddSeconds(Interlocked.Increment(ref _seconds));
    }
}
