namespace DynamicIsland.Core.Shell;

/// <summary>Island states (guide §4.1).</summary>
public enum IslandState
{
    /// <summary>Hidden by the user (tray "Hide").</summary>
    Hidden,
    Collapsed,

    /// <summary>Hover preview (D07).</summary>
    Peek,
    Expanded,

    /// <summary>Expanded and kept open until unpinned or Escape.</summary>
    Pinned,

    /// <summary>Auto-hidden while a fullscreen app is in front (D06).</summary>
    SuspendedFullscreen,

    /// <summary>Paused from the tray: hidden, optional work frozen.</summary>
    Paused,
}

public enum IslandInput
{
    PointerEnter,
    PointerLeave,

    /// <summary>Hover debounce elapsed while the pointer stayed inside (raised by the state machine itself).</summary>
    HoverElapsed,
    Click,
    Escape,
    TogglePin,
    FullscreenStarted,
    FullscreenEnded,
    Pause,
    Resume,
    Show,
    Hide,
}

/// <param name="RestoreState">Visible state to return to after fullscreen, pause or hide ends.</param>
/// <param name="PointerInside">Pointer currently over the visible island.</param>
/// <param name="FullscreenActive">A fullscreen app is in front (tracked even while paused or hidden).</param>
public readonly record struct IslandSnapshot(IslandState State, IslandState RestoreState, bool PointerInside, bool FullscreenActive)
{
    public static IslandSnapshot Initial { get; } = new(IslandState.Collapsed, IslandState.Collapsed, PointerInside: false, FullscreenActive: false);

    public bool IsVisible => State is IslandState.Collapsed or IslandState.Peek or IslandState.Expanded or IslandState.Pinned;
}

/// <summary>
/// Deterministic transition function (D07, guide §4.1). Rules:
/// <list type="bullet">
/// <item>Collapsed: hover (after debounce) → Peek; click → Expanded.</item>
/// <item>Pointer leaving Peek or Expanded → Collapsed; Pinned ignores the pointer.</item>
/// <item>Escape → Collapsed from any visible state and removes the pin.</item>
/// <item>Pin keeps the island expanded until unpinned; unpinning returns to Expanded only while the pointer is inside.</item>
/// <item>Fullscreen, Pause and Hide hide the island and remember the state to restore. Transient states (Peek,
/// Expanded) restore as Collapsed because the pointer has moved on; Pinned restores as Pinned.</item>
/// <item>Resume or Show returns to SuspendedFullscreen instead if a fullscreen app is still in front.</item>
/// </list>
/// </summary>
public static class IslandTransitions
{
    public static IslandSnapshot Next(IslandSnapshot current, IslandInput input)
    {
        var s = input switch
        {
            IslandInput.PointerEnter => current with { PointerInside = true },
            IslandInput.PointerLeave => current with { PointerInside = false },
            IslandInput.FullscreenStarted => current with { FullscreenActive = true },
            IslandInput.FullscreenEnded => current with { FullscreenActive = false },
            _ => current,
        };

        return s.State switch
        {
            IslandState.Collapsed or IslandState.Peek or IslandState.Expanded or IslandState.Pinned => FromVisible(s, input),
            IslandState.SuspendedFullscreen => input switch
            {
                IslandInput.FullscreenEnded => s with { State = s.RestoreState },
                IslandInput.Pause => s with { State = IslandState.Paused },
                IslandInput.Hide => s with { State = IslandState.Hidden },
                _ => s,
            },
            IslandState.Paused => input switch
            {
                IslandInput.Resume => s with { State = s.FullscreenActive ? IslandState.SuspendedFullscreen : s.RestoreState },
                _ => s,
            },
            IslandState.Hidden => input switch
            {
                IslandInput.Show => s with { State = s.FullscreenActive ? IslandState.SuspendedFullscreen : s.RestoreState },
                IslandInput.Pause => s with { State = IslandState.Paused },
                _ => s,
            },
            _ => s,
        };
    }

    private static IslandSnapshot FromVisible(IslandSnapshot s, IslandInput input)
    {
        var restore = s.State == IslandState.Pinned ? IslandState.Pinned : IslandState.Collapsed;

        return input switch
        {
            IslandInput.HoverElapsed when s.State == IslandState.Collapsed && s.PointerInside => s with { State = IslandState.Peek },
            IslandInput.PointerLeave when s.State is IslandState.Peek or IslandState.Expanded => s with { State = IslandState.Collapsed },
            IslandInput.Click when s.State is IslandState.Collapsed or IslandState.Peek => s with { State = IslandState.Expanded },
            IslandInput.Escape => s with { State = IslandState.Collapsed, RestoreState = IslandState.Collapsed },
            IslandInput.TogglePin when s.State == IslandState.Pinned => s with { State = s.PointerInside ? IslandState.Expanded : IslandState.Collapsed },
            IslandInput.TogglePin => s with { State = IslandState.Pinned },
            IslandInput.FullscreenStarted => s with { State = IslandState.SuspendedFullscreen, RestoreState = restore },
            IslandInput.Pause => s with { State = IslandState.Paused, RestoreState = restore },
            IslandInput.Hide => s with { State = IslandState.Hidden, RestoreState = restore },
            _ => s,
        };
    }
}

/// <summary>
/// Visible island size per state, in DIPs before the user's appearance scale. The island is drawn as a notch attached to
/// the top edge; widths include its two 6-DIP concave shoulders.
/// </summary>
public static class IslandLayout
{
    public static readonly (double Width, double Height) Collapsed = (236, 32);
    public static readonly (double Width, double Height) Peek = (264, 38);
    public static readonly (double Width, double Height) Expanded = (420, 180);

    /// <summary>The window is always this large; everything outside the visible pill is transparent and click-through.</summary>
    public static (double Width, double Height) Max => Expanded;

    /// <returns><c>null</c> for states in which the window is hidden.</returns>
    public static (double Width, double Height)? For(IslandState state) => state switch
    {
        IslandState.Collapsed => Collapsed,
        IslandState.Peek => Peek,
        IslandState.Expanded or IslandState.Pinned => Expanded,
        _ => null,
    };
}
