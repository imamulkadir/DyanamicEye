namespace DynamicIsland.Core.PersonalData;

/// <summary>Why an archive cannot be restored (shown to the user as a short message; no file contents).</summary>
public enum BackupProblem
{
    None,
    NotAnArchive,
    ManifestMissingOrInvalid,
    UnexpectedEntries,
    TooLarge,
    ChecksumMismatch,
    DamagedContent,

    /// <summary>Settings or data were written by a newer version of Dynamic Island.</summary>
    NewerVersion,
}

/// <summary>What a backup archive contains, after full validation (ADR 0024 dry run).</summary>
/// <param name="StagingId">Identifies the validated, extracted files for <see cref="IDataBackupService.ScheduleRestoreAsync"/>.</param>
public sealed record BackupPreview(
    BackupProblem Problem,
    DateTimeOffset? CreatedUtc,
    string? AppVersion,
    int Notes,
    int Tasks,
    bool HasSettings,
    string? StagingId)
{
    public bool CanRestore => Problem == BackupProblem.None && StagingId is not null;

    public static BackupPreview Refused(BackupProblem problem) => new(problem, null, null, 0, 0, false, null);
}

public sealed record BackupExportResult(int Notes, int Tasks, long Bytes);

/// <summary>Guide §3 contract: backup, export and restore of notes, tasks and settings (ADR 0024).</summary>
public interface IDataBackupService
{
    /// <summary>Writes a new archive to <paramref name="destinationPath"/>.</summary>
    /// <exception cref="IOException">The destination exists or cannot be written.</exception>
    Task<BackupExportResult> ExportAsync(string destinationPath, CancellationToken cancellationToken);

    /// <summary>Validates an archive completely and stages its files; changes nothing.</summary>
    Task<BackupPreview> PreviewAsync(string archivePath, CancellationToken cancellationToken);

    /// <summary>Marks an approved preview for restore at the next start (the caller restarts the app).</summary>
    Task<bool> ScheduleRestoreAsync(BackupPreview approved, CancellationToken cancellationToken);
}
