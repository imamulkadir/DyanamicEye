namespace DynamicIsland.Core.Settings;

/// <summary>
/// Persists <see cref="AppSettings"/>. Corrupt or unreadable content never throws: it is preserved aside and
/// replaced by a backup or defaults. Environmental I/O failures (permissions, disk) do propagate.
/// </summary>
public interface ISettingsStore
{
    /// <summary>True when saving is disabled to protect a file this build must not overwrite.</summary>
    bool IsReadOnly { get; }

    Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Validates, then atomically replaces the stored settings.</summary>
    Task<SettingsSaveResult> SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}

/// <param name="Settings">Validated settings to use.</param>
/// <param name="Outcome">How they were obtained.</param>
/// <param name="Corrections">Validation repairs applied (setting names only).</param>
/// <param name="PreservedFilePath">Quarantined corrupt file or pre-migration backup, if one was written.</param>
public sealed record SettingsLoadResult(
    AppSettings Settings,
    SettingsLoadOutcome Outcome,
    IReadOnlyList<string> Corrections,
    string? PreservedFilePath = null);

public enum SettingsLoadOutcome
{
    /// <summary>Stored settings loaded (possibly with corrections).</summary>
    Loaded,

    /// <summary>No settings file yet; first run.</summary>
    CreatedDefaults,

    /// <summary>Older schema migrated and saved; original kept as backup.</summary>
    Migrated,

    /// <summary>Main file was corrupt and quarantined; previous good copy restored.</summary>
    RecoveredFromBackup,

    /// <summary>Main file (and backup) unusable; file quarantined and defaults used.</summary>
    ResetAfterCorruption,

    /// <summary>File was written by a newer version; left untouched, defaults used, saving disabled.</summary>
    NewerVersionReadOnly,

    /// <summary>Migration failed; file left untouched, defaults used, saving disabled.</summary>
    MigrationFailedReadOnly,
}

public enum SettingsSaveResult
{
    Saved,
    SkippedReadOnly,
}
