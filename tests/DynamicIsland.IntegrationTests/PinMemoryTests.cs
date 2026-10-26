using DynamicIsland.App;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicIsland.IntegrationTests;

/// <summary>The pin is remembered across restarts (P05-02, ADR 0027), with the real state machine.</summary>
public sealed class PinMemoryTests : IDisposable
{
    private readonly IslandStateMachine _state = new(TimeProvider.System, action => action());

    public void Dispose() => _state.Dispose();

    [Fact]
    public void A_saved_pin_is_restored_at_start()
    {
        var settings = new Settings(pinned: true);
        using var memory = new PinMemory(_state, settings, NullLogger<PinMemory>.Instance);

        memory.Start();

        Assert.Equal(IslandState.Pinned, _state.State);
        Assert.Equal(0, settings.Saves); // Restoring is not a change.
    }

    [Fact]
    public async Task Pinning_and_unpinning_are_saved_but_hiding_for_a_while_is_not()
    {
        var settings = new Settings(pinned: false);
        using var memory = new PinMemory(_state, settings, NullLogger<PinMemory>.Instance);
        memory.Start();
        Assert.Equal(IslandState.Collapsed, _state.State);

        _state.Fire(IslandInput.TogglePin);
        await memory.Pending;
        Assert.True(settings.Current.Behavior.Pinned);

        _state.Fire(IslandInput.FullscreenStarted); // Hidden behind a game: still pinned, nothing saved.
        _state.Fire(IslandInput.FullscreenEnded);
        _state.Fire(IslandInput.Hide);
        _state.Fire(IslandInput.Show);
        await memory.Pending;
        Assert.Equal(1, settings.Saves);

        _state.Fire(IslandInput.Escape); // Escape removes the pin.
        await memory.Pending;
        Assert.False(settings.Current.Behavior.Pinned);
        Assert.Equal(2, settings.Saves);
    }

    [Fact]
    public async Task A_failed_save_is_logged_and_tried_again_on_the_next_change()
    {
        var settings = new Settings(pinned: false) { Failure = new IOException("disk full") };
        using var memory = new PinMemory(_state, settings, NullLogger<PinMemory>.Instance);
        memory.Start();

        _state.Fire(IslandInput.TogglePin);
        await memory.Pending; // Does not throw: the pin still works for this session.
        Assert.Equal(IslandState.Pinned, _state.State);

        settings.Failure = null;
        _state.Fire(IslandInput.Hide);
        _state.Fire(IslandInput.Show);
        _state.Fire(IslandInput.TogglePin);
        _state.Fire(IslandInput.TogglePin);
        await memory.Pending;
        Assert.True(settings.Current.Behavior.Pinned);
    }

    private sealed class Settings(bool pinned) : ISettingsService
    {
        public event EventHandler<AppSettings>? Changed;

        public AppSettings Current { get; private set; } = new() { Behavior = new BehaviorSettings { Pinned = pinned } };

        public bool IsReadOnly => false;

        public Exception? Failure { get; set; }

        public int Saves { get; private set; }

        public async Task<SettingsSaveResult> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (Failure is not null)
            {
                throw Failure;
            }

            Current = change(Current);
            Saves++;
            Changed?.Invoke(this, Current);
            return SettingsSaveResult.Saved;
        }
    }
}
