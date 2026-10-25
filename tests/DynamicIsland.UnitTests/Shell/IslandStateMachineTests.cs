using DynamicIsland.Core.Shell;
using DynamicIsland.UnitTests.TestSupport;
using static DynamicIsland.Core.Shell.IslandInput;
using static DynamicIsland.Core.Shell.IslandState;

namespace DynamicIsland.UnitTests.Shell;

public sealed class IslandTransitionTableTests
{
    private static readonly IslandInput[] InputOrder =
        [PointerEnter, PointerLeave, HoverElapsed, Click, Escape, TogglePin, FullscreenStarted, FullscreenEnded, Pause, Resume, Show, Hide];

    /// <summary>
    /// Expected next state for every state × input, starting from: pointer outside, no fullscreen app, restore = Collapsed.
    /// Columns follow <see cref="InputOrder"/>.
    /// </summary>
    private static readonly Dictionary<IslandState, IslandState[]> Table = new()
    {
        //                     Enter      Leave      Hover      Click      Escape     Pin        FsStart              FsEnd      Pause   Resume     Show       Hide
        [Collapsed] = [Collapsed, Collapsed, Collapsed, Expanded, Collapsed, Pinned, SuspendedFullscreen, Collapsed, Paused, Collapsed, Collapsed, Hidden],
        [Peek] = [Peek, Collapsed, Peek, Expanded, Collapsed, Pinned, SuspendedFullscreen, Peek, Paused, Peek, Peek, Hidden],
        [Expanded] = [Expanded, Collapsed, Expanded, Expanded, Collapsed, Pinned, SuspendedFullscreen, Expanded, Paused, Expanded, Expanded, Hidden],
        [Pinned] = [Pinned, Pinned, Pinned, Pinned, Collapsed, Collapsed, SuspendedFullscreen, Pinned, Paused, Pinned, Pinned, Hidden],
        [Hidden] = [Hidden, Hidden, Hidden, Hidden, Hidden, Hidden, Hidden, Hidden, Paused, Hidden, Collapsed, Hidden],
        [SuspendedFullscreen] = [SuspendedFullscreen, SuspendedFullscreen, SuspendedFullscreen, SuspendedFullscreen, SuspendedFullscreen, SuspendedFullscreen, SuspendedFullscreen, Collapsed, Paused, SuspendedFullscreen, SuspendedFullscreen, Hidden],
        [Paused] = [Paused, Paused, Paused, Paused, Paused, Paused, Paused, Paused, Paused, Collapsed, Paused, Paused],
    };

    public static TheoryData<IslandState, IslandInput, IslandState> AllTransitions()
    {
        var data = new TheoryData<IslandState, IslandInput, IslandState>();
        foreach (var (state, row) in Table)
        {
            for (var i = 0; i < InputOrder.Length; i++)
            {
                data.Add(state, InputOrder[i], row[i]);
            }
        }

        return data;
    }

    [Fact]
    public void Table_covers_every_state_and_input()
    {
        Assert.Equal(Enum.GetValues<IslandState>().Order(), Table.Keys.Order());
        Assert.Equal(Enum.GetValues<IslandInput>().Order(), InputOrder.Order());
        Assert.All(Table.Values, row => Assert.Equal(InputOrder.Length, row.Length));
    }

    [Theory]
    [MemberData(nameof(AllTransitions))]
    public void Transition(IslandState from, IslandInput input, IslandState expected)
    {
        var start = new IslandSnapshot(from, Collapsed, PointerInside: false, FullscreenActive: false);

        Assert.Equal(expected, IslandTransitions.Next(start, input).State);
    }
}

public sealed class IslandTransitionScenarioTests
{
    [Fact]
    public void Hover_elapsed_with_the_pointer_inside_opens_the_preview()
    {
        var snapshot = Run(PointerEnter, HoverElapsed);

        Assert.Equal(Peek, snapshot.State);
    }

    [Fact]
    public void Pinned_island_comes_back_pinned_after_a_fullscreen_app()
    {
        var snapshot = Run(Click, TogglePin, FullscreenStarted);
        Assert.Equal(SuspendedFullscreen, snapshot.State);

        Assert.Equal(Pinned, IslandTransitions.Next(snapshot, FullscreenEnded).State);
    }

    [Fact]
    public void Transient_expansion_comes_back_collapsed_after_a_fullscreen_app()
    {
        Assert.Equal(Collapsed, Run(Click, FullscreenStarted, FullscreenEnded).State);
    }

    [Fact]
    public void Resume_while_a_fullscreen_app_is_still_in_front_stays_suspended()
    {
        var snapshot = Run(TogglePin, FullscreenStarted, Pause, Resume);
        Assert.Equal(SuspendedFullscreen, snapshot.State);

        Assert.Equal(Pinned, IslandTransitions.Next(snapshot, FullscreenEnded).State);
    }

    [Fact]
    public void Fullscreen_ending_while_paused_resumes_to_the_visible_state()
    {
        Assert.Equal(Collapsed, Run(FullscreenStarted, Pause, FullscreenEnded, Resume).State);
    }

    [Fact]
    public void Show_while_a_fullscreen_app_is_in_front_stays_suspended()
    {
        Assert.Equal(SuspendedFullscreen, Run(Hide, FullscreenStarted, Show).State);
    }

    [Fact]
    public void Unpinning_with_the_pointer_inside_keeps_the_island_expanded()
    {
        Assert.Equal(Expanded, Run(PointerEnter, Click, TogglePin, TogglePin).State);
    }

    [Fact]
    public void Escape_clears_the_pin_so_fullscreen_restores_collapsed()
    {
        Assert.Equal(Collapsed, Run(TogglePin, Escape, FullscreenStarted, FullscreenEnded).State);
    }

    [Fact]
    public void Hidden_pinned_island_returns_pinned_on_show()
    {
        Assert.Equal(Pinned, Run(TogglePin, Hide, Show).State);
    }

    private static IslandSnapshot Run(params IslandInput[] inputs) =>
        inputs.Aggregate(IslandSnapshot.Initial, IslandTransitions.Next);
}

public sealed class IslandStateMachineHoverTests : IDisposable
{
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly IslandStateMachine _machine;
    private readonly List<IslandState> _changes = [];

    public IslandStateMachineHoverTests()
    {
        _machine = new IslandStateMachine(_time, action => action());
        _machine.StateChanged += (_, e) => _changes.Add(e.Current);
    }

    public void Dispose() => _machine.Dispose();

    [Fact]
    public void Preview_opens_only_after_the_hover_delay()
    {
        _machine.Fire(PointerEnter);
        _time.Advance(IslandStateMachine.HoverDelay - TimeSpan.FromMilliseconds(1));
        Assert.Equal(Collapsed, _machine.State);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(Peek, _machine.State);
        Assert.Equal([Peek], _changes);
    }

    [Fact]
    public void Brushing_past_never_opens_a_preview()
    {
        _machine.Fire(PointerEnter);
        _time.Advance(TimeSpan.FromMilliseconds(100));
        _machine.Fire(PointerLeave);
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(Collapsed, _machine.State);
        Assert.Empty(_changes);
    }

    [Fact]
    public void Click_during_the_hover_delay_expands_and_cancels_the_preview()
    {
        _machine.Fire(PointerEnter);
        _machine.Fire(Click);
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(Expanded, _machine.State);
        Assert.Equal([Expanded], _changes);
    }

    [Fact]
    public void Leaving_the_preview_collapses_immediately()
    {
        _machine.Fire(PointerEnter);
        _time.Advance(IslandStateMachine.HoverDelay);
        _machine.Fire(PointerLeave);

        Assert.Equal([Peek, Collapsed], _changes);
    }
}
