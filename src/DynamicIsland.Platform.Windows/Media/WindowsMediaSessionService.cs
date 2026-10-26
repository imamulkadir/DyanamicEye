using System.Runtime.InteropServices;
using DynamicIsland.Core.Media;
using Windows.Media.Control;
using MediaPlaybackStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace DynamicIsland.Platform.Windows.Media;

/// <summary>
/// <see cref="IMediaSessionService"/> over the Windows global media transport controls (ADR 0020). Event-driven: it
/// follows the session Windows calls current and raises <see cref="Changed"/> for its metadata and playback changes;
/// nothing is polled. Players can vanish at any moment, so WinRT errors are reported as "no session" rather than thrown.
/// </summary>
public sealed class WindowsMediaSessionService : IMediaSessionService
{
    private readonly Lock _gate = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    /// <summary>
    /// Bound for each call into Windows media sessions: a player or the media service can stop answering (seen on a dev
    /// PC: the request never completed), and a widget must then show "no media" rather than wait forever.
    /// </summary>
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

    public event EventHandler? Changed;

    public async Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        using var bounded = Bounded(cancellationToken);
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(bounded.Token).ConfigureAwait(false);
            if (manager is null)
            {
                return false;
            }

            lock (_gate)
            {
                _manager = manager;
            }

            manager.CurrentSessionChanged += OnCurrentSessionChanged;
            manager.SessionsChanged += OnSessionsChanged;
            FollowCurrentSession();
            return true;
        }
        catch (Exception ex) when (IsWinRtFailure(ex) || TimedOut(ex, cancellationToken))
        {
            return false;
        }
    }

    public async Task<MediaInfo?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var (manager, session) = Snapshot();
        if (manager is null || session is null)
        {
            return null;
        }

        using var bounded = Bounded(cancellationToken);
        try
        {
            var playback = session.GetPlaybackInfo();
            if (playback is null || playback.PlaybackStatus == MediaPlaybackStatus.Closed)
            {
                return null; // A player that is shutting down.
            }

            var properties = await session.TryGetMediaPropertiesAsync().AsTask(bounded.Token).ConfigureAwait(false);
            var title = properties?.Title?.Trim() ?? string.Empty;
            var artist = properties?.Artist?.Trim() ?? string.Empty;
            if (title.Length == 0 && artist.Length == 0)
            {
                return null; // A stale session with nothing to show.
            }

            var controls = playback.Controls;
            var allowed = (controls.IsPlayEnabled ? MediaControls.Play : MediaControls.None)
                | (controls.IsPauseEnabled ? MediaControls.Pause : MediaControls.None)
                | (controls.IsNextEnabled ? MediaControls.Next : MediaControls.None)
                | (controls.IsPreviousEnabled ? MediaControls.Previous : MediaControls.None);
            var state = playback.PlaybackStatus switch
            {
                MediaPlaybackStatus.Playing => MediaPlaybackState.Playing,
                MediaPlaybackStatus.Paused => MediaPlaybackState.Paused,
                MediaPlaybackStatus.Stopped => MediaPlaybackState.Stopped,
                _ => MediaPlaybackState.Unknown,
            };

            // Identifies the art without reading it; kept in memory only, never logged.
            var thumbnailKey = properties?.Thumbnail is null ? null : string.Join('\u001f', title, artist, properties.AlbumTitle ?? string.Empty);
            return new MediaInfo(title, artist, state, allowed, manager.GetSessions().Count, thumbnailKey);
        }
        catch (Exception ex) when (IsWinRtFailure(ex) || TimedOut(ex, cancellationToken))
        {
            return null;
        }
    }

    public async Task<byte[]?> GetThumbnailAsync(int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        var (_, session) = Snapshot();
        if (session is null)
        {
            return null;
        }

        using var bounded = Bounded(cancellationToken);
        try
        {
            var properties = await session.TryGetMediaPropertiesAsync().AsTask(bounded.Token).ConfigureAwait(false);
            if (properties?.Thumbnail is not { } thumbnail)
            {
                return null;
            }

            using var stream = await thumbnail.OpenReadAsync().AsTask(bounded.Token).ConfigureAwait(false);
            if (stream.Size == 0 || stream.Size > (ulong)maxBytes)
            {
                return null; // Unusually large art is skipped rather than loaded (ADR 0020).
            }

            using var input = stream.AsStreamForRead();
            using var buffer = new MemoryStream((int)stream.Size);
            await input.CopyToAsync(buffer, bounded.Token).ConfigureAwait(false);
            return buffer.Length <= maxBytes ? buffer.ToArray() : null;
        }
        catch (Exception ex) when (IsWinRtFailure(ex) || ex is IOException || TimedOut(ex, cancellationToken))
        {
            return null;
        }
    }

    private static CancellationTokenSource Bounded(CancellationToken cancellationToken)
    {
        var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(CallTimeout);
        return bounded;
    }

    /// <summary>Our own <see cref="CallTimeout"/> fired (not the caller's cancellation).</summary>
    private static bool TimedOut(Exception ex, CancellationToken cancellationToken) =>
        ex is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    /// <summary>
    /// Players refuse commands for a moment while they switch tracks (seen with Media Player: "next" sent right after a
    /// track change is refused). A command the player says it supports is therefore retried a few times, about 1 s in all.
    /// </summary>
    public static readonly TimeSpan CommandRetryDelay = TimeSpan.FromMilliseconds(250);

    public const int CommandAttempts = 5;

    public async Task<bool> SendAsync(MediaCommand command, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var (_, session) = Snapshot();
            if (session is null)
            {
                return false;
            }

            using var bounded = Bounded(cancellationToken);
            try
            {
                if (await SendOnceAsync(session, command, bounded.Token).ConfigureAwait(false))
                {
                    return true;
                }

                if (attempt == CommandAttempts || !IsSupported(session, command))
                {
                    return false;
                }
            }
            catch (Exception ex) when (IsWinRtFailure(ex) || TimedOut(ex, cancellationToken))
            {
                return false;
            }

            await Task.Delay(CommandRetryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        GlobalSystemMediaTransportControlsSessionManager? manager;
        lock (_gate)
        {
            manager = _manager;
            _manager = null;
        }

        if (manager is not null)
        {
            manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            manager.SessionsChanged -= OnSessionsChanged;
        }

        Follow(null);
    }

    private static Task<bool> SendOnceAsync(GlobalSystemMediaTransportControlsSession session, MediaCommand command, CancellationToken cancellationToken)
    {
        var operation = command switch
        {
            MediaCommand.TogglePlayPause => session.TryTogglePlayPauseAsync(),
            MediaCommand.Next => session.TrySkipNextAsync(),
            MediaCommand.Previous => session.TrySkipPreviousAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };
        return operation.AsTask(cancellationToken);
    }

    private static bool IsSupported(GlobalSystemMediaTransportControlsSession session, MediaCommand command)
    {
        var controls = session.GetPlaybackInfo()?.Controls;
        return controls is not null && command switch
        {
            MediaCommand.TogglePlayPause => controls.IsPlayPauseToggleEnabled || controls.IsPlayEnabled || controls.IsPauseEnabled,
            MediaCommand.Next => controls.IsNextEnabled,
            MediaCommand.Previous => controls.IsPreviousEnabled,
            _ => false,
        };
    }

    /// <summary>Players disconnect without warning: these are expected, not bugs.</summary>
    private static bool IsWinRtFailure(Exception ex) => ex is COMException or InvalidOperationException or UnauthorizedAccessException;

    private (GlobalSystemMediaTransportControlsSessionManager? Manager, GlobalSystemMediaTransportControlsSession? Session) Snapshot()
    {
        lock (_gate)
        {
            return (_manager, _session);
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => FollowCurrentSession();

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => FollowCurrentSession();

    private void FollowCurrentSession()
    {
        GlobalSystemMediaTransportControlsSession? current = null;
        try
        {
            current = Snapshot().Manager?.GetCurrentSession();
        }
        catch (Exception ex) when (IsWinRtFailure(ex))
        {
            // Treated as "no session".
        }

        Follow(current);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Follow(GlobalSystemMediaTransportControlsSession? session)
    {
        GlobalSystemMediaTransportControlsSession? previous;
        lock (_gate)
        {
            previous = _session;
            if (ReferenceEquals(previous, session))
            {
                return;
            }

            _session = session;
        }

        if (previous is not null)
        {
            previous.MediaPropertiesChanged -= OnSessionChanged;
            previous.PlaybackInfoChanged -= OnPlaybackChanged;
        }

        if (session is not null)
        {
            session.MediaPropertiesChanged += OnSessionChanged;
            session.PlaybackInfoChanged += OnPlaybackChanged;
        }
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);

    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);
}
