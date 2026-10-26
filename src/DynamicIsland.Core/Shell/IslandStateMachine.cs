using DynamicIsland.Core.Display;

namespace DynamicIsland.Core.Shell;

public sealed class IslandStateChangedEventArgs(IslandState previous, IslandState current) : EventArgs
{
    public IslandState Previous { get; } = previous;

    public IslandState Current { get; } = current;
}

/// <summary>Guide §3 contract: the island's interaction state.</summary>
public interface IIslandStateMachine
{
    event EventHandler<IslandStateChangedEventArgs>? StateChanged;

    IslandState State { get; }

    /// <summary>The user's pin is on, also while the island is hidden for a while (see <see cref="IslandSnapshot.IsPinned"/>).</summary>
    bool IsPinned => State == IslandState.Pinned;

    /// <summary>Applies an input. Call on the UI thread.</summary>
    void Fire(IslandInput input);
}

/// <summary>
/// Wraps <see cref="IslandTransitions"/> with the hover debounce: <see cref="IslandInput.PointerEnter"/> on a collapsed
/// island starts a <see cref="HoverDelay"/> timer whose elapse is posted back to the UI thread as
/// <see cref="IslandInput.HoverElapsed"/>; leaving earlier cancels it, so brushing past never opens a preview.
/// </summary>
public sealed class IslandStateMachine : IIslandStateMachine, IDisposable
{
    public static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(250);

    private readonly Debouncer _hover;
    private IslandSnapshot _snapshot = IslandSnapshot.Initial;

    /// <param name="post">Marshals timer callbacks to the thread that calls <see cref="Fire"/> (UI dispatcher).</param>
    public IslandStateMachine(TimeProvider timeProvider, Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(post);
        _hover = new Debouncer(HoverDelay, timeProvider, () => post(() => Fire(IslandInput.HoverElapsed)));
    }

    public event EventHandler<IslandStateChangedEventArgs>? StateChanged;

    public IslandState State => _snapshot.State;

    public IslandSnapshot Snapshot => _snapshot;

    public bool IsPinned => _snapshot.IsPinned;

    public void Fire(IslandInput input)
    {
        var previous = _snapshot;
        _snapshot = IslandTransitions.Next(previous, input);

        if (input == IslandInput.PointerEnter && _snapshot.State == IslandState.Collapsed)
        {
            _hover.Trigger();
        }
        else if (input == IslandInput.PointerLeave || _snapshot.State != IslandState.Collapsed)
        {
            _hover.Cancel();
        }

        if (previous.State != _snapshot.State)
        {
            StateChanged?.Invoke(this, new IslandStateChangedEventArgs(previous.State, _snapshot.State));
        }
    }

    public void Dispose() => _hover.Dispose();
}
