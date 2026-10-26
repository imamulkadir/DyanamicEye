using DynamicIsland.Core.Components;
using DynamicIsland.Core.Media;
using DynamicIsland.Widgets;

namespace DynamicIsland.UnitTests.Widgets;

/// <summary>The media widget (P04-03, ADR 0020) against a scripted media session service.</summary>
public sealed class MediaWidgetTests : IDisposable
{
    private readonly FakeMedia _media = new();
    private readonly MediaWidget _widget;
    private readonly Context _context = new();
    private readonly CancellationTokenSource _stop = new();

    public MediaWidgetTests() => _widget = new MediaWidget(_media);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _widget.Dispose();
    }

    [Fact]
    public async Task Shows_the_current_track_with_art_and_supported_controls()
    {
        _media.Current = Track("Song", "Artist", sessions: 3, art: "a");
        _ = Start(); // Ends with cancellation in Dispose.

        var snapshot = await WaitForAsync(s => s is not null);

        Assert.Equal("Song", snapshot!.Title);
        Assert.Equal("Artist", snapshot.Artist);
        Assert.Equal(2, snapshot.OtherSessions);
        Assert.Equal([1, 2, 3], snapshot.Art);
        Assert.Equal(MediaControls.Pause | MediaControls.Next, snapshot.Controls);
        Assert.Equal("healthy", _context.Last);
    }

    [Fact]
    public async Task Nothing_playing_shows_nothing_and_reports_degraded()
    {
        _ = Start(); // Ends with cancellation in Dispose.

        await WaitUntilAsync(() => _context.Last == "no-media");

        Assert.Null(_widget.Snapshot);
    }

    [Fact]
    public async Task System_changes_update_the_snapshot_and_art_is_read_only_when_it_changes()
    {
        _media.Current = Track("One", "A", art: "x");
        _ = Start(); // Ends with cancellation in Dispose.
        await WaitForAsync(s => s?.Title == "One");

        _media.Current = Track("One", "A", art: "x", state: MediaPlaybackState.Paused);
        _media.RaiseChanged();
        await WaitForAsync(s => s?.State == MediaPlaybackState.Paused);
        Assert.Equal(1, _media.ThumbnailReads);

        _media.Current = Track("Two", "B", art: "y");
        _media.RaiseChanged();
        await WaitForAsync(s => s?.Title == "Two");
        Assert.Equal(2, _media.ThumbnailReads);

        _media.Current = null; // The player closed.
        _media.RaiseChanged();
        await WaitForAsync(s => s is null);
    }

    [Fact]
    public async Task Missing_or_oversized_art_is_left_out()
    {
        _media.Current = Track("Song", "Artist", art: "big");
        _media.Thumbnail = null; // The service refuses art above the cap.
        _ = Start(); // Ends with cancellation in Dispose.

        var snapshot = await WaitForAsync(s => s is not null);

        Assert.Null(snapshot!.Art);
        Assert.Null(snapshot.ArtKey);
        Assert.Equal(MediaWidget.MaxArtBytes, _media.LastMaxBytes);
    }

    [Fact]
    public void Art_cap_admits_the_uncompressed_bitmap_media_player_hands_out()
    {
        // Found with a real Media Player session (2026-10-26): a 768×768 32-bit BMP of 2,359,446 bytes.
        Assert.True(MediaWidget.MaxArtBytes >= 2_359_446);
    }

    [Fact]
    public async Task Commands_reach_the_current_session_and_unknown_ones_are_refused()
    {
        Assert.True(await _widget.ExecuteAsync(MediaWidget.NextCommand, Ct));
        Assert.True(await _widget.ExecuteAsync(MediaWidget.PlayPauseCommand, Ct));
        Assert.True(await _widget.ExecuteAsync(MediaWidget.PreviousCommand, Ct));
        Assert.False(await _widget.ExecuteAsync("shuffle", Ct));

        Assert.Equal([MediaCommand.Next, MediaCommand.TogglePlayPause, MediaCommand.Previous], _media.Commands);
    }

    [Fact]
    public async Task Without_media_sessions_it_idles_without_retrying()
    {
        _media.Available = false;
        var run = Start();

        await WaitUntilAsync(() => _context.Last == "media-unavailable");
        await Task.Delay(100, Ct);

        Assert.False(run.IsCompleted);
        Assert.Equal(0, _media.Reads);
    }

    [Fact]
    public async Task Stopping_unsubscribes_and_release_drops_the_track()
    {
        _media.Current = Track("Song", "Artist");
        var run = Start();
        await WaitForAsync(s => s is not null);

        await _stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        _widget.Release();

        Assert.Null(_widget.Snapshot);
        Assert.Equal(0, _media.Subscribers);
    }

    private static MediaInfo Track(string title, string artist, int sessions = 1, string? art = null, MediaPlaybackState state = MediaPlaybackState.Playing) =>
        new(title, artist, state, MediaControls.Pause | MediaControls.Next, sessions, art);

    private Task Start() => Task.Run(() => _widget.RunAsync(_context, _stop.Token), Ct);

    private async Task<MediaSnapshot?> WaitForAsync(Func<MediaSnapshot?, bool> condition)
    {
        await WaitUntilAsync(() => condition(_widget.Snapshot as MediaSnapshot));
        return _widget.Snapshot as MediaSnapshot;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
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
        public string? Last { get; private set; }

        public void ReportHealthy() => Last = "healthy";

        public void ReportDegraded(string reasonCode) => Last = reasonCode;
    }

    private sealed class FakeMedia : IMediaSessionService
    {
        private EventHandler? _changed;

        public event EventHandler? Changed
        {
            add
            {
                _changed += value;
                Subscribers++;
            }

            remove
            {
                _changed -= value;
                Subscribers--;
            }
        }

        public bool Available { get; set; } = true;

        public MediaInfo? Current { get; set; }

        public byte[]? Thumbnail { get; set; } = [1, 2, 3];

        public int Subscribers { get; private set; }

        public int Reads { get; private set; }

        public int ThumbnailReads { get; private set; }

        public int LastMaxBytes { get; private set; }

        public List<MediaCommand> Commands { get; } = [];

        public void RaiseChanged() => _changed?.Invoke(this, EventArgs.Empty);

        public Task<bool> StartAsync(CancellationToken cancellationToken) => Task.FromResult(Available);

        public Task<MediaInfo?> GetCurrentAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(Current);
        }

        public Task<byte[]?> GetThumbnailAsync(int maxBytes, CancellationToken cancellationToken)
        {
            ThumbnailReads++;
            LastMaxBytes = maxBytes;
            return Task.FromResult(Thumbnail);
        }

        public Task<bool> SendAsync(MediaCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.FromResult(true);
        }

        public void Dispose()
        {
        }
    }
}
