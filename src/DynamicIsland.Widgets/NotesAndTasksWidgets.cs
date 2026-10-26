using System.Globalization;
using DynamicIsland.Core.Components;
using DynamicIsland.Core.PersonalData;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Widgets;

namespace DynamicIsland.Widgets;

/// <param name="Items">Shown tasks: open ones oldest first, then recently completed.</param>
/// <param name="LastResult">Result of the latest user command, for a short message in the view.</param>
public sealed record TasksSnapshot(IReadOnlyList<TaskItem> Items, int OpenCount, int DoneCount, bool ReadOnly, PersonalDataResult? LastResult);

/// <param name="Items">The latest notes, newest first.</param>
/// <param name="Total">All notes in the database (for "+N more").</param>
/// <param name="LastResult">Result of the latest user command, for a short message in the view.</param>
public sealed record NotesSnapshot(IReadOnlyList<Note> Items, int Total, bool ReadOnly, PersonalDataResult? LastResult);

/// <summary>
/// Shared loop for the notes and tasks widgets (ADR 0023): reloads when the store reports a change, otherwise idle.
/// Commands carry text and ids in memory only; nothing about them is logged.
/// </summary>
public abstract class PersonalDataWidget(IPersonalDataStore store) : IWidget, IDisposable
{
    public const string AddCommand = "add:";
    public const string EditCommand = "edit:";
    public const string ToggleCommand = "toggle:";
    public const string DeleteCommand = "delete:";

    private readonly Lock _gate = new();
    private CancellationTokenSource _wake = new();
    private object? _snapshot;
    private PersonalDataResult? _lastResult;

    public event EventHandler? SnapshotChanged;

    public abstract string Id { get; }

    public string Name => WidgetRegistry.ComponentName(Id);

    public object? Snapshot => Volatile.Read(ref _snapshot);

    protected IPersonalDataStore Store => store;

    protected PersonalDataResult? LastResult => _lastResult;

    /// <summary><c>edit:&lt;id&gt;:&lt;text&gt;</c> (the text may contain colons).</summary>
    public static string Edit(long id, string text) => EditCommand + id.ToString(CultureInfo.InvariantCulture) + ":" + text;

    public static string Toggle(long id) => ToggleCommand + id.ToString(CultureInfo.InvariantCulture);

    public static string Delete(long id) => DeleteCommand + id.ToString(CultureInfo.InvariantCulture);

    public async Task RunAsync(IComponentContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        store.Changed += OnStoreChanged;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                CancellationToken wake;
                lock (_gate)
                {
                    wake = _wake.Token;
                }

                Volatile.Write(ref _snapshot, await LoadAsync(cancellationToken).ConfigureAwait(false));
                SnapshotChanged?.Invoke(this, EventArgs.Empty);
                context.ReportHealthy();

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, wake);
                try
                {
                    await Task.Delay(Timeout.Infinite, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Woken by a change.
                }
            }
        }
        finally
        {
            store.Changed -= OnStoreChanged;
        }
    }

    public async Task<bool> ExecuteAsync(string command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await RunCommandAsync(command, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return false;
        }

        _lastResult = result;
        Wake(); // Refresh so the view can show a refused change (invalid, limit, read-only).
        return result == PersonalDataResult.Saved;
    }

    public void Release()
    {
        Volatile.Write(ref _snapshot, null);
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _wake.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    protected abstract Task<object> LoadAsync(CancellationToken cancellationToken);

    protected abstract Task<PersonalDataResult> AddAsync(string text, CancellationToken cancellationToken);

    protected abstract Task<PersonalDataResult> EditAsync(long id, string text, CancellationToken cancellationToken);

    protected abstract Task<PersonalDataResult> DeleteAsync(long id, CancellationToken cancellationToken);

    protected virtual Task<PersonalDataResult>? ToggleAsync(long id, CancellationToken cancellationToken) => null;

    private async Task<PersonalDataResult?> RunCommandAsync(string command, CancellationToken cancellationToken)
    {
        if (command.StartsWith(AddCommand, StringComparison.Ordinal))
        {
            return await AddAsync(command[AddCommand.Length..], cancellationToken).ConfigureAwait(false);
        }

        if (command.StartsWith(EditCommand, StringComparison.Ordinal))
        {
            var rest = command[EditCommand.Length..];
            var colon = rest.IndexOf(':', StringComparison.Ordinal);
            return colon > 0 && TryId(rest[..colon], out var id) ? await EditAsync(id, rest[(colon + 1)..], cancellationToken).ConfigureAwait(false) : null;
        }

        if (command.StartsWith(DeleteCommand, StringComparison.Ordinal) && TryId(command[DeleteCommand.Length..], out var deleteId))
        {
            return await DeleteAsync(deleteId, cancellationToken).ConfigureAwait(false);
        }

        if (command.StartsWith(ToggleCommand, StringComparison.Ordinal) && TryId(command[ToggleCommand.Length..], out var toggleId)
            && ToggleAsync(toggleId, cancellationToken) is { } toggle)
        {
            return await toggle.ConfigureAwait(false);
        }

        return null;
    }

    private static bool TryId(string text, out long id) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;

    private void OnStoreChanged(object? sender, EventArgs e) => Wake();

    private void Wake()
    {
        CancellationTokenSource previous;
        lock (_gate)
        {
            previous = _wake;
            _wake = new CancellationTokenSource();
        }

        // Not disposed here: the run loop may be linking to its token right now.
        previous.Cancel();
    }
}

/// <summary>Task list (guide P04-06): open tasks oldest first, then recently completed ones; at most <see cref="Shown"/>.</summary>
public sealed class TasksWidget(IPersonalDataStore store) : PersonalDataWidget(store)
{
    public const int Shown = 6;

    public override string Id => WidgetIds.Tasks;

    protected override async Task<object> LoadAsync(CancellationToken cancellationToken)
    {
        var items = await Store.GetTasksAsync(Shown, cancellationToken).ConfigureAwait(false);
        var (open, done) = await Store.CountTasksAsync(cancellationToken).ConfigureAwait(false);
        return new TasksSnapshot(items, open, done, Store.IsReadOnly, LastResult);
    }

    protected override Task<PersonalDataResult> AddAsync(string text, CancellationToken cancellationToken) => Store.AddTaskAsync(text, cancellationToken);

    protected override Task<PersonalDataResult> EditAsync(long id, string text, CancellationToken cancellationToken) => Store.EditTaskAsync(id, text, cancellationToken);

    protected override Task<PersonalDataResult> DeleteAsync(long id, CancellationToken cancellationToken) => Store.DeleteTaskAsync(id, cancellationToken);

    protected override async Task<PersonalDataResult>? ToggleAsync(long id, CancellationToken cancellationToken)
    {
        var current = (await Store.GetTasksAsync(PersonalDataRules.MaxTasks, cancellationToken).ConfigureAwait(false)).FirstOrDefault(t => t.Id == id);
        return current is null
            ? PersonalDataResult.NotFound
            : await Store.SetTaskDoneAsync(id, !current.Done, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Quick notes (guide P04-06): the latest <see cref="Shown"/> notes, newest first.</summary>
public sealed class NotesWidget(IPersonalDataStore store) : PersonalDataWidget(store)
{
    public const int Shown = 3;

    public override string Id => WidgetIds.Notes;

    protected override async Task<object> LoadAsync(CancellationToken cancellationToken)
    {
        var items = await Store.GetNotesAsync(Shown, cancellationToken).ConfigureAwait(false);
        var total = await Store.CountNotesAsync(cancellationToken).ConfigureAwait(false);
        return new NotesSnapshot(items, total, Store.IsReadOnly, LastResult);
    }

    protected override Task<PersonalDataResult> AddAsync(string text, CancellationToken cancellationToken) => Store.AddNoteAsync(text, cancellationToken);

    protected override Task<PersonalDataResult> EditAsync(long id, string text, CancellationToken cancellationToken) => Store.EditNoteAsync(id, text, cancellationToken);

    protected override Task<PersonalDataResult> DeleteAsync(long id, CancellationToken cancellationToken) => Store.DeleteNoteAsync(id, cancellationToken);
}
