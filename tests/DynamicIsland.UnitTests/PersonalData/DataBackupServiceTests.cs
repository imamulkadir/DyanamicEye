using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using DynamicIsland.Core.PersonalData;
using DynamicIsland.Infrastructure.PersonalData;
using DynamicIsland.Infrastructure.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicIsland.UnitTests.PersonalData;

/// <summary>Backup, export and restore (P04-07, ADR 0024) on real files in a temporary folder.</summary>
public sealed class DataBackupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DynamicIsland.UnitTests", Guid.NewGuid().ToString("N"));
    private readonly string _data;
    private readonly string _archive;
    private readonly List<SqlitePersonalDataStore> _stores = [];

    public DataBackupServiceTests()
    {
        _data = Path.Combine(_root, "data");
        _archive = Path.Combine(_root, "backup.zip");
        Directory.CreateDirectory(_data);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_writes_a_manifest_with_checksums_and_a_consistent_database_while_the_store_is_open()
    {
        var (store, backup) = Create();
        await store.AddNoteAsync("note one", Ct);
        await store.AddTaskAsync("task one", Ct);
        await File.WriteAllTextAsync(Path.Combine(_data, JsonSettingsStore.FileName), """{ "schemaVersion": 1 }""", Ct);

        var result = await backup.ExportAsync(_archive, Ct);

        Assert.Equal((1, 1), (result.Notes, result.Tasks));
        using var zip = ZipFile.OpenRead(_archive);
        Assert.Equal(["islands.db", "manifest.json", "settings.json"], zip.Entries.Select(e => e.FullName).Order());
        var manifest = JsonNode.Parse(zip.GetEntry("manifest.json")!.Open())!;
        Assert.Equal("dynamicisland-backup", (string?)manifest["format"]);
        Assert.Equal(2, manifest["files"]!.AsArray().Count);
        Assert.All(manifest["files"]!.AsArray(), f => Assert.Equal(64, ((string)f!["sha256"]!).Length));
        Assert.DoesNotContain(Directory.GetFiles(_root), f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Export_never_overwrites_an_existing_file()
    {
        var (_, backup) = Create();
        await File.WriteAllTextAsync(_archive, "keep me", Ct);

        await Assert.ThrowsAsync<IOException>(() => backup.ExportAsync(_archive, Ct));
        Assert.Equal("keep me", await File.ReadAllTextAsync(_archive, Ct));
    }

    [Fact]
    public async Task Preview_and_restore_replace_data_at_next_start_and_keep_a_rollback_copy()
    {
        var (store, backup) = Create();
        await store.AddNoteAsync("from the backup", Ct);
        await File.WriteAllTextAsync(Path.Combine(_data, JsonSettingsStore.FileName), """{ "schemaVersion": 1, "marker": "backup" }""", Ct);
        await backup.ExportAsync(_archive, Ct);

        // Life goes on after the backup.
        await store.AddNoteAsync("written later", Ct);
        await File.WriteAllTextAsync(Path.Combine(_data, JsonSettingsStore.FileName), """{ "schemaVersion": 1, "marker": "later" }""", Ct);

        var preview = await backup.PreviewAsync(_archive, Ct);
        Assert.True(preview.CanRestore);
        Assert.Equal((1, 0, true), (preview.Notes, preview.Tasks, preview.HasSettings));
        Assert.Equal(2, await store.CountNotesAsync(Ct)); // The preview changes nothing.

        Assert.True(await backup.ScheduleRestoreAsync(preview, Ct));
        CloseStores(); // The app restarts.

        Assert.True(DataBackupService.ApplyPendingRestore(_data, TimeProvider.System, NullLogger.Instance));

        var (restored, _) = Create();
        Assert.Equal(["from the backup"], (await restored.GetNotesAsync(10, Ct)).Select(n => n.Text));
        Assert.Contains("\"backup\"", await File.ReadAllTextAsync(Path.Combine(_data, JsonSettingsStore.FileName), Ct), StringComparison.Ordinal);
        var rollback = Assert.Single(Directory.GetDirectories(_data, "rollback-*"));
        Assert.Contains("\"later\"", await File.ReadAllTextAsync(Path.Combine(rollback, JsonSettingsStore.FileName), Ct), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_data, DataBackupService.PendingFolderName)));
        Assert.Null(DataBackupService.ApplyPendingRestore(_data, TimeProvider.System, NullLogger.Instance)); // Nothing left to apply.
    }

    [Fact]
    public async Task A_restore_that_cannot_be_applied_keeps_the_previous_data()
    {
        var (store, backup) = Create();
        await store.AddNoteAsync("backup", Ct);
        await backup.ExportAsync(_archive, Ct);
        await store.AddNoteAsync("current", Ct);
        Assert.True(await backup.ScheduleRestoreAsync(await backup.PreviewAsync(_archive, Ct), Ct));
        CloseStores();

        // Something holds the live database open, so it cannot be moved aside.
        using (new FileStream(Path.Combine(_data, SqlitePersonalDataStore.FileName), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(DataBackupService.ApplyPendingRestore(_data, TimeProvider.System, NullLogger.Instance));
        }

        var (current, _) = Create();
        Assert.Equal(2, await current.CountNotesAsync(Ct));
        Assert.True(File.Exists(Path.Combine(_data, DataBackupService.PendingFolderName, SqlitePersonalDataStore.FileName)), "The pending restore is kept for diagnosis.");
    }

    [Fact]
    public async Task Tampered_content_is_refused()
    {
        var (store, backup) = Create();
        await store.AddTaskAsync("x", Ct);
        await backup.ExportAsync(_archive, Ct);
        await RewriteEntryAsync("islands.db", bytes => [.. bytes.Take(bytes.Length - 1), (byte)(bytes[^1] ^ 0xFF)]);

        Assert.Equal(BackupProblem.ChecksumMismatch, (await backup.PreviewAsync(_archive, Ct)).Problem);
    }

    [Fact]
    public async Task Settings_or_data_from_a_newer_version_are_refused()
    {
        var (store, backup) = Create();
        await store.AddTaskAsync("x", Ct);
        await File.WriteAllTextAsync(Path.Combine(_data, JsonSettingsStore.FileName), """{ "schemaVersion": 99 }""", Ct);
        await backup.ExportAsync(_archive, Ct);

        Assert.Equal(BackupProblem.NewerVersion, (await backup.PreviewAsync(_archive, Ct)).Problem);
    }

    [Theory]
    [InlineData("not a zip at all", BackupProblem.NotAnArchive)]
    [InlineData("missing-manifest", BackupProblem.ManifestMissingOrInvalid)]
    [InlineData("extra-entry", BackupProblem.UnexpectedEntries)]
    [InlineData("wrong-format", BackupProblem.ManifestMissingOrInvalid)]
    [InlineData("bad-database", BackupProblem.DamagedContent)]
    public async Task Broken_archives_are_refused_with_a_reason(string kind, BackupProblem expected)
    {
        var (_, backup) = Create();
        if (kind == "not a zip at all")
        {
            await File.WriteAllTextAsync(_archive, kind, Ct);
        }
        else
        {
            using var zip = ZipFile.Open(_archive, ZipArchiveMode.Create);
            var db = Encoding.UTF8.GetBytes("not a database");
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(db));
            if (kind != "missing-manifest")
            {
                var format = kind == "wrong-format" ? "something-else" : DataBackupService.Format;
                Write(zip, "manifest.json", $$"""{ "format": "{{format}}", "formatVersion": 1, "files": [ { "name": "islands.db", "bytes": {{db.Length}}, "sha256": "{{hash}}" } ] }""");
            }

            Write(zip, "islands.db", db);
            if (kind == "extra-entry")
            {
                Write(zip, "../evil.dll", "x");
            }
        }

        var preview = await backup.PreviewAsync(_archive, Ct);

        Assert.Equal(expected, preview.Problem);
        Assert.False(preview.CanRestore);
        Assert.False(await backup.ScheduleRestoreAsync(preview, Ct));
    }

    [Fact]
    public async Task Oversized_entries_are_refused_without_unpacking_them()
    {
        var (_, backup) = Create();
        using (var zip = ZipFile.Open(_archive, ZipArchiveMode.Create))
        {
            Write(zip, "manifest.json", """{ "format": "dynamicisland-backup", "formatVersion": 1, "files": [] }""");
            var entry = zip.CreateEntry("islands.db", CompressionLevel.SmallestSize);
            using var stream = entry.Open();
            var zeros = new byte[1024 * 1024];
            for (var i = 0; i < 65; i++)
            {
                stream.Write(zeros); // 65 MB of zeros compress to almost nothing: a small "ZIP bomb".
            }
        }

        Assert.Equal(BackupProblem.TooLarge, (await backup.PreviewAsync(_archive, Ct)).Problem);
        Assert.DoesNotContain(Directory.EnumerateFiles(_data, "*", SearchOption.AllDirectories), f => new FileInfo(f).Length > DataBackupService.MaxEntryBytes);
    }

    [Fact]
    public async Task Damaged_compressed_data_is_refused_instead_of_failing()
    {
        var (store, backup) = Create();
        await store.AddTaskAsync("x", Ct);
        await backup.ExportAsync(_archive, Ct);
        SetCompressionMethod(_archive, "islands.db", 99); // Unknown method: reading the entry throws InvalidDataException.

        var preview = await backup.PreviewAsync(_archive, Ct);

        Assert.Equal(BackupProblem.DamagedContent, preview.Problem);
        Assert.False(preview.CanRestore);
    }

    [Theory]
    [InlineData("UPDATE notes SET created_utc = 'not a time';")]
    [InlineData("UPDATE notes SET text = 'a' || char(7) || 'b';")]
    [InlineData("UPDATE notes SET text = replace(hex(zeroblob(1100)), '0', 'n');")] // 4400 characters
    [InlineData("UPDATE tasks SET done = 5;")]
    public async Task Rows_the_app_could_not_have_written_are_refused(string damage)
    {
        var (store, backup) = Create();
        await store.AddNoteAsync("note", Ct);
        await store.AddTaskAsync("task", Ct);
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(_data, SqlitePersonalDataStore.FileName)};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = damage;
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        await backup.ExportAsync(_archive, Ct);

        Assert.Equal(BackupProblem.DamagedContent, (await backup.PreviewAsync(_archive, Ct)).Problem);
    }

    [Theory]
    [InlineData("2.0.0-beta.1+abc", "2.0.0-beta.1+abc")]
    [InlineData("1.0 <b>Click here</b>", null)]
    [InlineData("", null)]
    public async Task Only_a_plausible_app_version_is_shown_for_a_backup(string written, string? shown)
    {
        var (store, _) = Create();
        await store.AddTaskAsync("x", Ct);
        var backup = new DataBackupService(_data, store, written, TimeProvider.System, NullLogger<DataBackupService>.Instance);
        await backup.ExportAsync(_archive, Ct);

        Assert.Equal(shown, (await backup.PreviewAsync(_archive, Ct)).AppVersion);
    }

    private (SqlitePersonalDataStore Store, DataBackupService Backup) Create()
    {
        var store = new SqlitePersonalDataStore(_data, TimeProvider.System, NullLogger<SqlitePersonalDataStore>.Instance);
        _stores.Add(store);
        return (store, new DataBackupService(_data, store, "1.2.3", TimeProvider.System, NullLogger<DataBackupService>.Instance));
    }

    private void CloseStores()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

        _stores.Clear();
        SqliteConnection.ClearAllPools();
    }

    private async Task RewriteEntryAsync(string name, Func<byte[], byte[]> change)
    {
        using var zip = ZipFile.Open(_archive, ZipArchiveMode.Update);
        var entry = zip.GetEntry(name)!;
        byte[] bytes;
        using (var stream = entry.Open())
        using (var copy = new MemoryStream())
        {
            await stream.CopyToAsync(copy, Ct);
            bytes = copy.ToArray();
        }

        entry.Delete();
        Write(zip, name, change(bytes));
    }

    /// <summary>Patches the compression method of <paramref name="name"/> in both its local and central ZIP headers.</summary>
    private static void SetCompressionMethod(string archive, string name, ushort method)
    {
        var bytes = File.ReadAllBytes(archive);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var patched = 0;
        for (var i = 0; i + 46 < bytes.Length; i++)
        {
            var signature = BitConverter.ToUInt32(bytes, i);
            var (methodOffset, nameOffset) = signature switch
            {
                0x04034b50 => (8, 30), // Local file header.
                0x02014b50 => (10, 46), // Central directory header.
                _ => (-1, -1),
            };
            if (methodOffset >= 0 && bytes.AsSpan(i + nameOffset).StartsWith(nameBytes))
            {
                BitConverter.TryWriteBytes(bytes.AsSpan(i + methodOffset, 2), method);
                patched++;
            }
        }

        Assert.Equal(2, patched);
        File.WriteAllBytes(archive, bytes);
    }

    private static void Write(ZipArchive zip, string name, string text) => Write(zip, name, Encoding.UTF8.GetBytes(text));

    private static void Write(ZipArchive zip, string name, byte[] bytes)
    {
        using var stream = zip.CreateEntry(name).Open();
        stream.Write(bytes);
    }
}
