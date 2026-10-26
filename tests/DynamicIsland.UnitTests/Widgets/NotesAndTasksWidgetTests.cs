using DynamicIsland.Core.Components;
using DynamicIsland.Core.PersonalData;
using DynamicIsland.Infrastructure.PersonalData;
using DynamicIsland.Widgets;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicIsland.UnitTests.Widgets;

/// <summary>Notes and tasks widgets (P04-06, ADR 0023) over a real store in a temporary folder.</summary>
public sealed class NotesAndTasksWidgetTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "DynamicIsland.UnitTests", Guid.NewGuid().ToString("N"));
    private readonly SqlitePersonalDataStore _store;
    private readonly TasksWidget _tasks;
    private readonly NotesWidget _notes;
    private readonly CancellationTokenSource _stop = new();

    public NotesAndTasksWidgetTests()
    {
        _store = new SqlitePersonalDataStore(_folder, TimeProvider.System, NullLogger<SqlitePersonalDataStore>.Instance);
        _tasks = new TasksWidget(_store);
        _notes = new NotesWidget(_store);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _tasks.Dispose();
        _notes.Dispose();
        _store.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task Task_commands_add_toggle_edit_and_delete()
    {
        _ = Start(_tasks); // Ends with cancellation in Dispose.
        await WaitAsync(() => _tasks.Snapshot is TasksSnapshot { Items.Count: 0 });

        Assert.True(await _tasks.ExecuteAsync(PersonalDataWidget.AddCommand + "Buy milk", Ct));
        var added = (await WaitForTasksAsync(s => s.Items.Count == 1)).Items[0];

        Assert.True(await _tasks.ExecuteAsync(PersonalDataWidget.Toggle(added.Id), Ct));
        await WaitForTasksAsync(s => s.Items[0].Done && s.DoneCount == 1);

        Assert.True(await _tasks.ExecuteAsync(PersonalDataWidget.Edit(added.Id, "Buy oat milk: 2 litres"), Ct));
        await WaitForTasksAsync(s => s.Items[0].Text == "Buy oat milk: 2 litres");

        Assert.True(await _tasks.ExecuteAsync(PersonalDataWidget.Delete(added.Id), Ct));
        await WaitForTasksAsync(s => s.Items.Count == 0);
    }

    [Fact]
    public async Task Refused_changes_are_reported_in_the_snapshot()
    {
        _ = Start(_tasks); // Ends with cancellation in Dispose.

        Assert.False(await _tasks.ExecuteAsync(PersonalDataWidget.AddCommand + "   ", Ct));

        await WaitForTasksAsync(s => s.LastResult == PersonalDataResult.Invalid);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("toggle:abc")]
    [InlineData("delete:-4")]
    [InlineData("edit:5")]
    [InlineData("edit::text")]
    [InlineData("rename:1")]
    public async Task Malformed_commands_are_refused(string command)
    {
        Assert.False(await _tasks.ExecuteAsync(command, Ct));
        Assert.False(await _notes.ExecuteAsync(command, Ct));
    }

    [Fact]
    public async Task Notes_show_the_latest_three_and_count_the_rest()
    {
        _ = Start(_notes); // Ends with cancellation in Dispose.
        for (var i = 1; i <= 5; i++)
        {
            Assert.True(await _notes.ExecuteAsync(PersonalDataWidget.AddCommand + $"note {i}", Ct));
        }

        var snapshot = await WaitForNotesAsync(s => s.Total == 5 && s.Items.Count == 3);

        Assert.Equal(["note 5", "note 4", "note 3"], snapshot.Items.Select(n => n.Text));
        Assert.False(await _notes.ExecuteAsync(PersonalDataWidget.Toggle(snapshot.Items[0].Id), Ct)); // Notes have no "done".
    }

    [Fact]
    public async Task Changes_from_one_widget_reach_the_other_through_the_store()
    {
        _ = Start(_notes); // Ends with cancellation in Dispose.
        await WaitAsync(() => _notes.Snapshot is NotesSnapshot);

        await _store.AddNoteAsync("added elsewhere", Ct);

        await WaitForNotesAsync(s => s.Items.Count == 1);
    }

    private Task Start(PersonalDataWidget widget) => Task.Run(() => widget.RunAsync(new Context(), _stop.Token), Ct);

    private async Task<TasksSnapshot> WaitForTasksAsync(Func<TasksSnapshot, bool> condition)
    {
        await WaitAsync(() => _tasks.Snapshot is TasksSnapshot s && condition(s));
        return (TasksSnapshot)_tasks.Snapshot!;
    }

    private async Task<NotesSnapshot> WaitForNotesAsync(Func<NotesSnapshot, bool> condition)
    {
        await WaitAsync(() => _notes.Snapshot is NotesSnapshot s && condition(s));
        return (NotesSnapshot)_notes.Snapshot!;
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition.");
            await Task.Delay(10, Ct);
        }
    }

    private sealed class Context : IComponentContext
    {
        public void ReportHealthy()
        {
        }

        public void ReportDegraded(string reasonCode)
        {
        }
    }
}
