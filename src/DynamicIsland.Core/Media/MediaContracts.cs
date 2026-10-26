namespace DynamicIsland.Core.Media;

public enum MediaPlaybackState
{
    Unknown,
    Playing,
    Paused,
    Stopped,
}

/// <summary>Controls the current player says it supports right now (unsupported ones are shown disabled).</summary>
[Flags]
public enum MediaControls
{
    None = 0,
    Play = 1,
    Pause = 2,
    Next = 4,
    Previous = 8,
}

public enum MediaCommand
{
    TogglePlayPause,
    Next,
    Previous,
}

/// <summary>
/// What the system reports for the current media session (guide P04-03, ADR 0020). Title and artist are personal data:
/// shown on screen only, never logged or put in diagnostics.
/// </summary>
/// <param name="SessionCount">All sessions known to Windows, including the current one.</param>
/// <param name="ThumbnailKey">Changes when the album art changes, so callers only reload it then.</param>
public sealed record MediaInfo(
    string Title,
    string Artist,
    MediaPlaybackState State,
    MediaControls Controls,
    int SessionCount,
    string? ThumbnailKey);

/// <summary>Guide §3 contract: the system's media sessions (Windows global media transport controls).</summary>
public interface IMediaSessionService : IDisposable
{
    /// <summary>Raised (on any thread) when the current session, its metadata, playback state or controls change.</summary>
    event EventHandler? Changed;

    /// <summary>Connects to the system. Returns <c>false</c> when media sessions are not available on this PC.</summary>
    Task<bool> StartAsync(CancellationToken cancellationToken);

    /// <summary>The session Windows considers current, or <c>null</c> when nothing usable is playing or paused.</summary>
    Task<MediaInfo?> GetCurrentAsync(CancellationToken cancellationToken);

    /// <summary>The current album art (image bytes, at most <paramref name="maxBytes"/>), or <c>null</c>.</summary>
    Task<byte[]?> GetThumbnailAsync(int maxBytes, CancellationToken cancellationToken);

    /// <summary>Sends a command to the current session; <c>false</c> when there is none or the player refused it.</summary>
    Task<bool> SendAsync(MediaCommand command, CancellationToken cancellationToken);
}
