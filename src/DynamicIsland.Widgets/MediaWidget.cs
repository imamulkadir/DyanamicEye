using DynamicIsland.Core.Components;
using DynamicIsland.Core.Media;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Widgets;

namespace DynamicIsland.Widgets;

/// <param name="Art">Album art bytes (at most <see cref="MediaWidget.MaxArtBytes"/>), or <c>null</c>; decoded by the view off the UI thread.</param>
/// <param name="ArtKey">Changes when the art changes (decode cache key); in memory only.</param>
/// <param name="OtherSessions">Other players Windows knows about besides the current one.</param>
public sealed record MediaSnapshot(
    string Title,
    string Artist,
    MediaPlaybackState State,
    MediaControls Controls,
    int OtherSessions,
    byte[]? Art,
    string? ArtKey);

/// <summary>
/// Media widget (guide P04-03, ADR 0020): shows the session Windows calls current (title, artist, art) with
/// previous/play-pause/next for the controls that player supports. Event-driven: it refreshes only when the system
/// reports a change, never polls, and shows nothing when no player has anything loaded. Track details are never logged.
/// </summary>
public sealed class MediaWidget(IMediaSessionService media) : IWidget, IDisposable
{
    /// <summary>
    /// Album art larger than this is skipped (not loaded) to bound memory. Players may hand out uncompressed bitmaps:
    /// Media Player gives a 768×768 32-bit BMP (2.36 MB), so the cap allows up to about 1700×1700 uncompressed.
    /// </summary>
    public const int MaxArtBytes = 12 * 1024 * 1024;

    public const string PlayPauseCommand = "play-pause";
    public const string NextCommand = "next";
    public const string PreviousCommand = "previous";

    /// <summary>Bursts of system events (metadata, playback, art arriving separately) are read once.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(60);

    private readonly Lock _gate = new();
    private CancellationTokenSource _wake = new();
    private MediaSnapshot? _snapshot;

    public event EventHandler? SnapshotChanged;

    public string Id => WidgetIds.Media;

    public string Name => WidgetRegistry.ComponentName(WidgetIds.Media);

    public object? Snapshot => Volatile.Read(ref _snapshot);

    public async Task RunAsync(IComponentContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!await media.StartAsync(cancellationToken).ConfigureAwait(false))
        {
            // No media sessions on this PC: stay idle (nothing to retry) until stopped.
            context.ReportDegraded("media-unavailable");
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return;
        }

        media.Changed += OnMediaChanged;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                CancellationToken wake;
                lock (_gate)
                {
                    wake = _wake.Token;
                }

                var info = await media.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
                await PublishAsync(info, cancellationToken).ConfigureAwait(false);
                if (info is null)
                {
                    context.ReportDegraded("no-media");
                }
                else
                {
                    context.ReportHealthy();
                }

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, wake);
                try
                {
                    await Task.Delay(Timeout.Infinite, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(Settle, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            media.Changed -= OnMediaChanged;
        }
    }

    public Task<bool> ExecuteAsync(string command, CancellationToken cancellationToken) => command switch
    {
        PlayPauseCommand => media.SendAsync(MediaCommand.TogglePlayPause, cancellationToken),
        NextCommand => media.SendAsync(MediaCommand.Next, cancellationToken),
        PreviousCommand => media.SendAsync(MediaCommand.Previous, cancellationToken),
        _ => Task.FromResult(false),
    };

    public void OnSystemChanged() => Wake();

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

        media.Dispose();
    }

    private async Task PublishAsync(MediaInfo? info, CancellationToken cancellationToken)
    {
        var previous = Volatile.Read(ref _snapshot);
        MediaSnapshot? next = null;
        if (info is not null)
        {
            // Read the art only when it changed.
            var art = info.ThumbnailKey is null ? null
                : info.ThumbnailKey == previous?.ArtKey ? previous.Art
                : await media.GetThumbnailAsync(MaxArtBytes, cancellationToken).ConfigureAwait(false);
            next = new MediaSnapshot(info.Title, info.Artist, info.State, info.Controls, Math.Max(0, info.SessionCount - 1), art, art is null ? null : info.ThumbnailKey);
        }

        if (!Equals(next, previous))
        {
            Volatile.Write(ref _snapshot, next);
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnMediaChanged(object? sender, EventArgs e) => Wake();

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
