using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Infrastructure.Settings;

/// <summary>
/// Stores settings as <c>settings.json</c> (guide §4.3).
/// <list type="bullet">
/// <item>Writes are atomic: temp file in the same folder, flushed, then <see cref="File.Replace(string, string, string?)"/>;
/// the replaced file is kept as <c>settings.json.bak</c>.</item>
/// <item>Corrupt, oversized or unrecognized files are moved to <c>settings.corrupt-*.json</c> (newest few kept),
/// then the backup is tried, then defaults.</item>
/// <item>Older schemas are copied to <c>settings.v{N}.backup.json</c>, migrated and saved.</item>
/// <item>Newer schemas or failed migrations are never overwritten: defaults are returned and saving is disabled.</item>
/// </list>
/// Load and save are serialized; one instance should own the folder.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore, IDisposable
{
    public const string FileName = "settings.json";
    public const string BackupFileName = "settings.json.bak";
    public const string QuarantinePrefix = "settings.corrupt-";
    public const int MaxFileBytes = 1024 * 1024;
    public const int MaxQuarantinedFiles = 5;

    private const string SchemaVersionProperty = "schemaVersion";
    private const int MaxReplaceAttempts = 3;
    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        AllowDuplicateProperties = false,
        MaxDepth = 32,
    };

    private readonly string _directory;
    private readonly string _path;
    private readonly string _backupPath;
    private readonly Dictionary<int, ISettingsMigration> _migrations = [];
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _isReadOnly;

    public JsonSettingsStore(SettingsStoreOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DataDirectory);

        foreach (var migration in options.Migrations)
        {
            if (migration.FromVersion < 0 || migration.FromVersion >= AppSettings.CurrentSchemaVersion)
            {
                throw new ArgumentException($"Settings migration from version {migration.FromVersion} is outside the supported range.", nameof(options));
            }

            if (!_migrations.TryAdd(migration.FromVersion, migration))
            {
                throw new ArgumentException($"Duplicate settings migration from version {migration.FromVersion}.", nameof(options));
            }
        }

        _directory = Path.GetFullPath(options.DataDirectory);
        _path = Path.Combine(_directory, FileName);
        _backupPath = Path.Combine(_directory, BackupFileName);
        _timeProvider = timeProvider;
    }

    public bool IsReadOnly => _isReadOnly;

    public async Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SettingsSaveResult> SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isReadOnly)
            {
                return SettingsSaveResult.SkippedReadOnly;
            }

            await WriteAtomicAsync(SettingsValidator.Normalize(settings).Settings, cancellationToken).ConfigureAwait(false);
            return SettingsSaveResult.Saved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<SettingsLoadResult> LoadCoreAsync(CancellationToken cancellationToken)
    {
        _isReadOnly = false;

        if (!File.Exists(_path))
        {
            return new SettingsLoadResult(new AppSettings(), SettingsLoadOutcome.CreatedDefaults, []);
        }

        var document = await TryReadDocumentAsync(_path, cancellationToken).ConfigureAwait(false);
        if (document is not null)
        {
            var loaded = await TryMaterializeAsync(document, _path, cancellationToken).ConfigureAwait(false);
            if (loaded is not null)
            {
                return loaded;
            }
        }

        var quarantinedPath = Quarantine();

        if (File.Exists(_backupPath))
        {
            var backupDocument = await TryReadDocumentAsync(_backupPath, cancellationToken).ConfigureAwait(false);
            var recovered = backupDocument is null
                ? null
                : await TryMaterializeAsync(backupDocument, _backupPath, cancellationToken).ConfigureAwait(false);

            if (recovered is { Outcome: SettingsLoadOutcome.Loaded or SettingsLoadOutcome.Migrated })
            {
                await WriteAtomicAsync(recovered.Settings, cancellationToken).ConfigureAwait(false);
                return recovered with { Outcome = SettingsLoadOutcome.RecoveredFromBackup, PreservedFilePath = quarantinedPath };
            }

            if (recovered is not null)
            {
                return recovered;
            }
        }

        return new SettingsLoadResult(new AppSettings(), SettingsLoadOutcome.ResetAfterCorruption, [], quarantinedPath);
    }

    /// <summary>Returns <c>null</c> when the document is not a usable settings file.</summary>
    private async Task<SettingsLoadResult?> TryMaterializeAsync(JsonObject document, string sourcePath, CancellationToken cancellationToken)
    {
        if (!TryGetSchemaVersion(document, out var version))
        {
            return null;
        }

        if (version > AppSettings.CurrentSchemaVersion)
        {
            _isReadOnly = true;
            return new SettingsLoadResult(new AppSettings(), SettingsLoadOutcome.NewerVersionReadOnly, []);
        }

        if (version == AppSettings.CurrentSchemaVersion)
        {
            var settings = TryDeserialize(document);
            if (settings is null)
            {
                return null;
            }

            var validation = SettingsValidator.Normalize(settings);
            return new SettingsLoadResult(validation.Settings, SettingsLoadOutcome.Loaded, validation.Corrections);
        }

        if (!HasMigrationPath(version))
        {
            return null;
        }

        var preservedPath = Path.Combine(_directory, $"settings.v{version.ToString(CultureInfo.InvariantCulture)}.backup.json");
        File.Copy(sourcePath, preservedPath, overwrite: true);

        AppSettings? migrated;
        try
        {
            migrated = TryDeserialize(Migrate(document, version));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A broken migration must not crash startup or destroy data: file stays as-is, saving is disabled.
            migrated = null;
        }

        if (migrated is null)
        {
            _isReadOnly = true;
            return new SettingsLoadResult(new AppSettings(), SettingsLoadOutcome.MigrationFailedReadOnly, [], preservedPath);
        }

        var migratedValidation = SettingsValidator.Normalize(migrated);
        await WriteAtomicAsync(migratedValidation.Settings, cancellationToken).ConfigureAwait(false);
        return new SettingsLoadResult(migratedValidation.Settings, SettingsLoadOutcome.Migrated, migratedValidation.Corrections, preservedPath);
    }

    private bool HasMigrationPath(int fromVersion)
    {
        for (var v = fromVersion; v < AppSettings.CurrentSchemaVersion; v++)
        {
            if (!_migrations.ContainsKey(v))
            {
                return false;
            }
        }

        return true;
    }

    private JsonObject Migrate(JsonObject document, int fromVersion)
    {
        for (var v = fromVersion; v < AppSettings.CurrentSchemaVersion; v++)
        {
            document = _migrations[v].Migrate(document);
            document[SchemaVersionProperty] = v + 1;
        }

        return document;
    }

    /// <summary>Missing version means a legacy unversioned file (version 0).</summary>
    private static bool TryGetSchemaVersion(JsonObject document, out int version)
    {
        if (!document.TryGetPropertyValue(SchemaVersionProperty, out var node) || node is null)
        {
            version = 0;
            return true;
        }

        if (node is JsonValue value && value.TryGetValue(out version) && version >= 0)
        {
            return true;
        }

        version = -1;
        return false;
    }

    private static AppSettings? TryDeserialize(JsonObject document)
    {
        try
        {
            return document.Deserialize(SettingsJson.AppSettingsTypeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<JsonObject?> TryReadDocumentAsync(string path, CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length > MaxFileBytes)
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > MaxFileBytes)
        {
            return null;
        }

        var json = bytes.AsSpan();
        if (json.StartsWith(Utf8Bom))
        {
            json = json[Utf8Bom.Length..];
        }

        try
        {
            return JsonNode.Parse(json, nodeOptions: null, DocumentOptions) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string Quarantine()
    {
        var stamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var target = Path.Combine(_directory, $"{QuarantinePrefix}{stamp}.json");
        for (var n = 1; File.Exists(target); n++)
        {
            target = Path.Combine(_directory, $"{QuarantinePrefix}{stamp}-{n.ToString(CultureInfo.InvariantCulture)}.json");
        }

        File.Move(_path, target);
        PruneQuarantine();
        return target;
    }

    private void PruneQuarantine()
    {
        var stale = Directory.GetFiles(_directory, QuarantinePrefix + "*.json")
            .Order(StringComparer.Ordinal)
            .SkipLast(MaxQuarantinedFiles);

        foreach (var file in stale)
        {
            File.Delete(file);
        }
    }

    private async Task WriteAtomicAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var tempPath = Path.Combine(_directory, $"{FileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            var streamOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            };

            var stream = new FileStream(tempPath, streamOptions);
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(stream, settings, SettingsJson.AppSettingsTypeInfo, cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            await ReplaceWithRetryAsync(tempPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DeleteIfExists(tempPath);
        }
    }

    private async Task ReplaceWithRetryAsync(string tempPath, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (File.Exists(_path))
                {
                    File.Replace(tempPath, _path, _backupPath, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, _path);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < MaxReplaceAttempts)
            {
                // Transient sharing violations (antivirus, indexer) are common on Windows; retry briefly.
                await Task.Delay(ReplaceRetryDelay * attempt, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover temp file is harmless and never read.
        }
    }
}
