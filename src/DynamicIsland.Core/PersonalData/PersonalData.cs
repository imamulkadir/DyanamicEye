namespace DynamicIsland.Core.PersonalData;

/// <summary>A note. <see cref="Text"/> is personal data: shown on screen only, never logged or put in diagnostics.</summary>
public sealed record Note(long Id, string Text, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc);

/// <summary>A task. <see cref="Text"/> is personal data (see <see cref="Note"/>).</summary>
public sealed record TaskItem(long Id, string Text, bool Done, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc, DateTimeOffset? CompletedUtc);

public enum PersonalDataResult
{
    Saved,

    /// <summary>The text broke a rule in <see cref="PersonalDataRules"/>; nothing changed.</summary>
    Invalid,

    /// <summary>The item no longer exists.</summary>
    NotFound,

    /// <summary>The limit on the number of notes or tasks was reached.</summary>
    LimitReached,

    /// <summary>The database belongs to a newer version and is open read-only (ADR 0023).</summary>
    ReadOnly,
}

/// <summary>Guide P04-06: local notes and tasks (ADR 0023). All writes are transactional; calls never block the UI thread.</summary>
public interface IPersonalDataStore
{
    /// <summary>Raised (on any thread) after notes or tasks changed.</summary>
    event EventHandler? Changed;

    bool IsReadOnly { get; }

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<Note>> GetNotesAsync(int max, CancellationToken cancellationToken);

    Task<int> CountNotesAsync(CancellationToken cancellationToken);

    /// <summary>Open tasks (oldest first), then completed tasks (most recently completed first).</summary>
    Task<IReadOnlyList<TaskItem>> GetTasksAsync(int max, CancellationToken cancellationToken);

    Task<(int Open, int Done)> CountTasksAsync(CancellationToken cancellationToken);

    Task<PersonalDataResult> AddNoteAsync(string text, CancellationToken cancellationToken);

    Task<PersonalDataResult> EditNoteAsync(long id, string text, CancellationToken cancellationToken);

    Task<PersonalDataResult> DeleteNoteAsync(long id, CancellationToken cancellationToken);

    Task<PersonalDataResult> AddTaskAsync(string text, CancellationToken cancellationToken);

    Task<PersonalDataResult> EditTaskAsync(long id, string text, CancellationToken cancellationToken);

    Task<PersonalDataResult> SetTaskDoneAsync(long id, bool done, CancellationToken cancellationToken);

    Task<PersonalDataResult> DeleteTaskAsync(long id, CancellationToken cancellationToken);
}

/// <summary>Validation shared by the store and the views (ADR 0023 §4).</summary>
public static class PersonalDataRules
{
    public const int MaxNoteLength = 2000;
    public const int MaxTaskLength = 300;
    public const int MaxNotes = 1000;
    public const int MaxTasks = 2000;

    /// <summary>Trimmed note text, or <c>null</c> when empty, too long or containing control characters other than line breaks.</summary>
    public static string? NormalizeNote(string? text) => Normalize(text, MaxNoteLength, allowLineBreaks: true);

    /// <summary>Trimmed single-line task text, or <c>null</c> when invalid.</summary>
    public static string? NormalizeTask(string? text) => Normalize(text, MaxTaskLength, allowLineBreaks: false);

    private static string? Normalize(string? text, int maxLength, bool allowLineBreaks)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > maxLength)
        {
            return null;
        }

        foreach (var c in trimmed)
        {
            if (char.IsControl(c) && !(allowLineBreaks && c is '\r' or '\n' or '\t'))
            {
                return null;
            }
        }

        return allowLineBreaks ? trimmed.Replace("\r\n", "\n", StringComparison.Ordinal) : trimmed;
    }
}
