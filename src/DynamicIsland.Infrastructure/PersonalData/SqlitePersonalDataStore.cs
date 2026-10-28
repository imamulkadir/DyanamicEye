using System.Globalization;
using DynamicIsland.Core.PersonalData;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.Infrastructure.PersonalData;

/// <summary>
/// Notes and tasks in <c>data\islands.db</c> (ADR 0023). All work is serialized and runs off the caller's thread; every
/// write is one transaction. A corrupt file is moved aside and replaced; a database from a newer version, or one whose
/// upgrade step failed (rolled back), is opened read-only. Logs carry event names, counts and error codes — never note or task text.
/// </summary>
public sealed partial class SqlitePersonalDataStore : IPersonalDataStore, IDisposable
{
    public const string FileName = "islands.db";
    public const int CurrentSchemaVersion = 1;
    public const int KeepCorruptCopies = 5;

    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;

    internal static readonly string[] DefaultMigrations =
    [
        // v0 → v1
        """
        CREATE TABLE notes (
            id INTEGER PRIMARY KEY,
            text TEXT NOT NULL,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL);
        CREATE TABLE tasks (
            id INTEGER PRIMARY KEY,
            text TEXT NOT NULL,
            done INTEGER NOT NULL DEFAULT 0,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL,
            completed_utc TEXT NULL);
        CREATE INDEX tasks_done ON tasks(done, completed_utc);
        """,
    ];

    private readonly string _directory;
    private readonly IReadOnlyList<string> _migrations;
    private readonly TimeProvider _time;
    private readonly ILogger<SqlitePersonalDataStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _connectionString;
    private bool _readOnly;

    public SqlitePersonalDataStore(string dataDirectory, TimeProvider time, ILogger<SqlitePersonalDataStore> logger)
        : this(dataDirectory, time, logger, DefaultMigrations)
    {
    }

    /// <summary>Tests rehearse a future schema with extra migrations (entry <c>v</c> upgrades version v to v + 1).</summary>
    internal SqlitePersonalDataStore(string dataDirectory, TimeProvider time, ILogger<SqlitePersonalDataStore> logger, IReadOnlyList<string> migrations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThan(migrations.Count, CurrentSchemaVersion);
        _directory = dataDirectory;
        _migrations = migrations;
        _time = time;
        _logger = logger;
    }

    public event EventHandler? Changed;

    public string DatabasePath => Path.Combine(_directory, FileName);

    public bool IsReadOnly => Volatile.Read(ref _readOnly);

    public Task<IReadOnlyList<Note>> GetNotesAsync(int max, CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<Note>>(connection =>
        {
            using var command = Command(connection, "SELECT id, text, created_utc, updated_utc FROM notes ORDER BY updated_utc DESC, id DESC LIMIT $max;", ("$max", max));
            using var reader = command.ExecuteReader();
            var notes = new List<Note>();
            while (reader.Read())
            {
                notes.Add(new Note(reader.GetInt64(0), reader.GetString(1), Time(reader.GetString(2)), Time(reader.GetString(3))));
            }

            return notes;
        }, cancellationToken);

    public Task<int> CountNotesAsync(CancellationToken cancellationToken) =>
        RunAsync(connection => Count(connection, "SELECT COUNT(*) FROM notes;"), cancellationToken);

    public Task<IReadOnlyList<TaskItem>> GetTasksAsync(int max, CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<TaskItem>>(connection =>
        {
            using var command = Command(
                connection,
                "SELECT id, text, done, created_utc, updated_utc, completed_utc FROM tasks ORDER BY done ASC, CASE WHEN done = 0 THEN created_utc END ASC, completed_utc DESC, id ASC LIMIT $max;",
                ("$max", max));
            using var reader = command.ExecuteReader();
            var tasks = new List<TaskItem>();
            while (reader.Read())
            {
                tasks.Add(new TaskItem(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetInt64(2) != 0,
                    Time(reader.GetString(3)),
                    Time(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : Time(reader.GetString(5))));
            }

            return tasks;
        }, cancellationToken);

    public Task<(int Open, int Done)> CountTasksAsync(CancellationToken cancellationToken) =>
        RunAsync(connection => (
            Count(connection, "SELECT COUNT(*) FROM tasks WHERE done = 0;"),
            Count(connection, "SELECT COUNT(*) FROM tasks WHERE done <> 0;")), cancellationToken);

    public Task<PersonalDataResult> AddNoteAsync(string text, CancellationToken cancellationToken) =>
        WriteAsync(PersonalDataRules.NormalizeNote(text), (connection, transaction, valid, now) =>
        {
            if (Count(connection, "SELECT COUNT(*) FROM notes;", transaction) >= PersonalDataRules.MaxNotes)
            {
                return PersonalDataResult.LimitReached;
            }

            Execute(connection, transaction, "INSERT INTO notes(text, created_utc, updated_utc) VALUES ($text, $now, $now);", ("$text", valid), ("$now", now));
            return PersonalDataResult.Saved;
        }, cancellationToken);

    public Task<PersonalDataResult> EditNoteAsync(long id, string text, CancellationToken cancellationToken) =>
        WriteAsync(PersonalDataRules.NormalizeNote(text), (connection, transaction, valid, now) =>
            Execute(connection, transaction, "UPDATE notes SET text = $text, updated_utc = $now WHERE id = $id;", ("$text", valid), ("$now", now), ("$id", id)) == 1
                ? PersonalDataResult.Saved
                : PersonalDataResult.NotFound, cancellationToken);

    public Task<PersonalDataResult> DeleteNoteAsync(long id, CancellationToken cancellationToken) =>
        WriteAsync(string.Empty, (connection, transaction, _, _) =>
            Execute(connection, transaction, "DELETE FROM notes WHERE id = $id;", ("$id", id)) == 1 ? PersonalDataResult.Saved : PersonalDataResult.NotFound, cancellationToken);

    public Task<PersonalDataResult> AddTaskAsync(string text, CancellationToken cancellationToken) =>
        WriteAsync(PersonalDataRules.NormalizeTask(text), (connection, transaction, valid, now) =>
        {
            if (Count(connection, "SELECT COUNT(*) FROM tasks;", transaction) >= PersonalDataRules.MaxTasks)
            {
                return PersonalDataResult.LimitReached;
            }

            Execute(connection, transaction, "INSERT INTO tasks(text, done, created_utc, updated_utc) VALUES ($text, 0, $now, $now);", ("$text", valid), ("$now", now));
            return PersonalDataResult.Saved;
        }, cancellationToken);

    public Task<PersonalDataResult> EditTaskAsync(long id, string text, CancellationToken cancellationToken) =>
        WriteAsync(PersonalDataRules.NormalizeTask(text), (connection, transaction, valid, now) =>
            Execute(connection, transaction, "UPDATE tasks SET text = $text, updated_utc = $now WHERE id = $id;", ("$text", valid), ("$now", now), ("$id", id)) == 1
                ? PersonalDataResult.Saved
                : PersonalDataResult.NotFound, cancellationToken);

    public Task<PersonalDataResult> SetTaskDoneAsync(long id, bool done, CancellationToken cancellationToken) =>
        WriteAsync(string.Empty, (connection, transaction, _, now) =>
            Execute(
                connection,
                transaction,
                "UPDATE tasks SET done = $done, completed_utc = CASE WHEN $done = 1 THEN $now ELSE NULL END, updated_utc = $now WHERE id = $id;",
                ("$done", done ? 1 : 0),
                ("$now", now),
                ("$id", id)) == 1
                ? PersonalDataResult.Saved
                : PersonalDataResult.NotFound, cancellationToken);

    public Task<PersonalDataResult> DeleteTaskAsync(long id, CancellationToken cancellationToken) =>
        WriteAsync(string.Empty, (connection, transaction, _, _) =>
            Execute(connection, transaction, "DELETE FROM tasks WHERE id = $id;", ("$id", id)) == 1 ? PersonalDataResult.Saved : PersonalDataResult.NotFound, cancellationToken);

    public void Dispose()
    {
        _gate.Dispose();
        SqliteConnection.ClearAllPools();
    }

    private static DateTimeOffset Time(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static SqliteCommand Command(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters) =>
        Command(connection, null, sql, parameters);

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }

    private static int Count(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = Command(connection, transaction, sql);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static int Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = Command(connection, transaction, sql, parameters);
        return command.ExecuteNonQuery();
    }

    private Task<PersonalDataResult> WriteAsync(
        string? validText,
        Func<SqliteConnection, SqliteTransaction, string, string, PersonalDataResult> write,
        CancellationToken cancellationToken)
    {
        if (validText is null)
        {
            return Task.FromResult(PersonalDataResult.Invalid);
        }

        return RunAsync(connection =>
        {
            if (Volatile.Read(ref _readOnly))
            {
                return PersonalDataResult.ReadOnly;
            }

            var now = _time.GetUtcNow().ToString("o", CultureInfo.InvariantCulture);
            using var transaction = connection.BeginTransaction();
            var result = write(connection, transaction, validText, now);
            if (result == PersonalDataResult.Saved)
            {
                transaction.Commit();
            }

            return result;
        }, cancellationToken, notifyOnSaved: true);
    }

    private async Task<T> RunAsync<T>(Func<SqliteConnection, T> work, CancellationToken cancellationToken, bool notifyOnSaved = false)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        T result;
        try
        {
            result = await Task.Run(() =>
            {
                EnsureOpen();
                using var connection = Open();
                return work(connection);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        if (notifyOnSaved && result is PersonalDataResult.Saved)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 3000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    /// <summary>First use: create or open, check integrity, migrate. Caller holds <see cref="_gate"/>.</summary>
    private void EnsureOpen()
    {
        if (_connectionString is not null)
        {
            return;
        }

        Directory.CreateDirectory(_directory);
        var path = DatabasePath;
        try
        {
            OpenAndMigrate(path);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteCorrupt or SqliteNotADatabase)
        {
            Quarantine(path, ex.SqliteErrorCode);
            OpenAndMigrate(path);
        }
    }

    private void OpenAndMigrate(string path)
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = true };
        using (var connection = new SqliteConnection(builder.ToString()))
        {
            connection.Open();
            using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA quick_check;";
                if (!string.Equals(check.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
                {
                    throw new SqliteException("quick_check failed", SqliteCorrupt);
                }
            }

            var version = Count(connection, "PRAGMA user_version;");
            var target = _migrations.Count;
            if (version > target)
            {
                // Written by a newer version: never downgrade or overwrite (like settings, ADR 0003).
                Volatile.Write(ref _readOnly, true);
                StoreLog.NewerSchemaReadOnly(_logger, version, target);
                builder.Mode = SqliteOpenMode.ReadOnly;
            }
            else
            {
                using (var wal = connection.CreateCommand())
                {
                    wal.CommandText = "PRAGMA journal_mode = WAL;";
                    wal.ExecuteNonQuery();
                }

                for (var v = version; v < target; v++)
                {
                    try
                    {
                        using var transaction = connection.BeginTransaction();
                        using (var migrate = Command(connection, transaction, _migrations[v]))
                        {
                            migrate.ExecuteNonQuery();
                        }

                        using (var stamp = Command(connection, transaction, $"PRAGMA user_version = {v + 1};"))
                        {
                            stamp.ExecuteNonQuery();
                        }

                        transaction.Commit();
                        StoreLog.Migrated(_logger, v, v + 1);
                    }
                    catch (SqliteException ex) when (ex.SqliteErrorCode is not (SqliteCorrupt or SqliteNotADatabase))
                    {
                        // The step's transaction rolled back, so the data is exactly as before (ADR 0036). Keep it
                        // readable but never write to a half-upgraded schema; the pre-update snapshot stays untouched.
                        Volatile.Write(ref _readOnly, true);
                        StoreLog.MigrationFailed(_logger, v, v + 1, ex.SqliteErrorCode);
                        builder.Mode = SqliteOpenMode.ReadOnly;
                        break;
                    }
                }
            }
        }

        _connectionString = builder.ToString();
    }

    /// <summary>Moves a corrupt database (and its WAL files) aside, keeping the newest <see cref="KeepCorruptCopies"/>.</summary>
    private void Quarantine(string path, int errorCode)
    {
        SqliteConnection.ClearAllPools();
        var stamp = _time.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var target = Path.Combine(_directory, $"islands.corrupt-{stamp}.db");
        File.Move(path, target, overwrite: true);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            if (File.Exists(path + suffix))
            {
                File.Move(path + suffix, target + suffix, overwrite: true);
            }
        }

        StoreLog.Quarantined(_logger, errorCode);
        foreach (var old in Directory.GetFiles(_directory, "islands.corrupt-*.db").OrderByDescending(f => f, StringComparer.Ordinal).Skip(KeepCorruptCopies))
        {
            File.Delete(old);
            File.Delete(old + "-wal");
            File.Delete(old + "-shm");
        }
    }

    private static partial class StoreLog
    {
        [LoggerMessage(EventId = 3100, EventName = "PersonalDataQuarantined", Level = LogLevel.Warning,
            Message = "Notes and tasks database was damaged (SQLite error {ErrorCode}); it was moved aside and a new one was created")]
        public static partial void Quarantined(ILogger logger, int errorCode);

        [LoggerMessage(EventId = 3101, EventName = "PersonalDataMigrated", Level = LogLevel.Information,
            Message = "Notes and tasks database upgraded from schema {From} to {To}")]
        public static partial void Migrated(ILogger logger, int from, int to);

        [LoggerMessage(EventId = 3102, EventName = "PersonalDataReadOnly", Level = LogLevel.Warning,
            Message = "Notes and tasks database has schema {Found}, newer than {Supported}; opened read-only")]
        public static partial void NewerSchemaReadOnly(ILogger logger, int found, int supported);

        [LoggerMessage(EventId = 3103, EventName = "PersonalDataMigrationFailed", Level = LogLevel.Error,
            Message = "Notes and tasks database upgrade from schema {From} to {To} failed (SQLite error {ErrorCode}); data unchanged, opened read-only")]
        public static partial void MigrationFailed(ILogger logger, int from, int to, int errorCode);
    }
}
