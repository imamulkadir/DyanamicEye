using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App;

/// <summary>
/// Remembers the pin across restarts (P05-02, ADR 0027): restores it at start and saves it when the user pins or unpins
/// (pin button, Ctrl+P, Escape). Hiding the island for a while (fullscreen, pause, hide) keeps the pin. UI thread only.
/// </summary>
public sealed class PinMemory(IIslandStateMachine state, ISettingsService settings, ILogger<PinMemory> logger) : IDisposable
{
    private bool? _saved; // null: unknown after a failed save, so the next change saves.

    /// <summary>Saves still in flight (tests).</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    public void Start()
    {
        _saved = settings.Current.Behavior.Pinned;
        if (_saved == true && !state.IsPinned && state.State == IslandState.Collapsed)
        {
            state.Fire(IslandInput.TogglePin);
        }

        state.StateChanged += OnStateChanged;
    }

    public void Dispose() => state.StateChanged -= OnStateChanged;

    private void OnStateChanged(object? sender, IslandStateChangedEventArgs e)
    {
        var pinned = state.IsPinned;
        if (pinned == _saved || settings.IsReadOnly)
        {
            return;
        }

        _saved = pinned;
        Pending = SaveAsync(Pending, pinned); // In order: quick pin/unpin clicks must end with the last one.
    }

    private async Task SaveAsync(Task previous, bool pinned)
    {
        await previous.ConfigureAwait(true);
        try
        {
            await settings.UpdateAsync(s => s with { Behavior = s.Behavior with { Pinned = pinned } }, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // The pin still works for this session; the next save tries again.
            _saved = null;
            AppLog.PinNotSaved(logger, ex.GetType().Name);
        }
    }
}
