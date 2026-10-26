using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.App;
using DynamicIsland.Core.Components;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Media;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using DynamicIsland.Core.Widgets;
using DynamicIsland.Platform.Windows.Media;
using DynamicIsland.TestSupport;
using DynamicIsland.Widgets;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicIsland.IntegrationTests;

/// <summary>Media widget (P04-03, ADR 0020): the real Windows adapter (read-only) and the view through the presenter.</summary>
public sealed class MediaWidgetIntegrationTests : IDisposable
{
    private readonly StaDispatcherThread _ui = new();
    private readonly LifecycleCoordinator _lifecycle = new(TimeProvider.System);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _ui.Dispose();
        _lifecycle.Dispose();
    }

    [Fact]
    public async Task Windows_media_sessions_connect_and_read_without_errors()
    {
        // Reads whatever is (or is not) playing on this PC; never sends a command to the user's player.
        using var service = new WindowsMediaSessionService();

        Assert.True(await service.StartAsync(Ct), "Global media transport controls should be available on Windows 11.");
        var current = await service.GetCurrentAsync(Ct);
        var art = await service.GetThumbnailAsync(MediaWidget.MaxArtBytes, Ct);

        Assert.True(current is null || current.SessionCount >= 1);
        Assert.True(art is null || art.Length <= MediaWidget.MaxArtBytes);
    }

    [Fact]
    public async Task View_shows_the_track_decodes_art_and_buttons_send_supported_commands()
    {
        var media = new ScriptedMedia
        {
            Current = new MediaInfo("Song title", "Artist", MediaPlaybackState.Playing, MediaControls.Pause | MediaControls.Next, 1, "art-1"),
            Thumbnail = Png(),
        };
        using var host = new ComponentHost(_lifecycle, new InMemoryNotificationService(TimeProvider.System), new DefaultFailureClassifier(), LaunchArguments.Default, TimeProvider.System);
        using var registry = new WidgetRegistry(host, [new MediaWidget(media)]);
        var view = _ui.Invoke(() => new NotchView());
        using var presenter = new WidgetPresenter(registry, view, new Settings(), new States(), _lifecycle, NullLogger<WidgetPresenter>.Instance);
        registry.Register([new WidgetSetting(WidgetIds.Media, true)]);
        host.StartAll();
        _ui.Invoke(presenter.Start);

        Assert.True(await WaitAsync(() => _ui.Invoke(() => Find<Image>(view) is not null)), "The decoded album art must appear.");
        var (texts, previous, playPause, next) = _ui.Invoke(() =>
        {
            var buttons = Descendants(view).OfType<Button>().ToDictionary(b => AutomationProperties.GetAutomationId(b));
            return (
                Descendants(view).OfType<TextBlock>().Select(t => t.Text).ToArray(),
                buttons["Media.previous"],
                buttons["Media.play-pause"],
                buttons["Media.next"]);
        });

        Assert.Contains("Song title", texts);
        Assert.Contains("Artist", texts);
        Assert.False(_ui.Invoke(() => previous.IsEnabled)); // The player does not support it.
        Assert.Equal("Pause", _ui.Invoke(() => AutomationProperties.GetName(playPause)));

        _ui.Invoke(() => next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)));
        _ui.Invoke(() => playPause.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)));

        Assert.True(await WaitAsync(() => media.Commands.Count == 2));
        Assert.Equal([MediaCommand.Next, MediaCommand.TogglePlayPause], media.Commands);
    }

    [Fact]
    public void Untagged_track_uses_two_title_lines_and_no_empty_artist_line()
    {
        var lines = _ui.Invoke(() =>
        {
            using var host = new ComponentHost(_lifecycle, new InMemoryNotificationService(TimeProvider.System), new DefaultFailureClassifier(), LaunchArguments.Default, TimeProvider.System);
            using var registry = new WidgetRegistry(host, []);
            using var presenter = new WidgetPresenter(registry, new NotchView(), new Settings(), new States(), _lifecycle, NullLogger<WidgetPresenter>.Instance);
            var view = presenter.ViewFor(
                new WidgetSlot(WidgetIds.Media, "Media", new ComponentStatus("widget-media", ComponentState.Healthy),
                    new MediaSnapshot("A long video title without tags [Official Music Video]", string.Empty, MediaPlaybackState.Playing, MediaControls.Pause, 0, null, null)))!;
            return Descendants(view).OfType<TextBlock>()
                .Where(t => t.Text.Length > 0 && t.Visibility == Visibility.Visible && t.FontFamily.Source != "Segoe Fluent Icons, Segoe MDL2 Assets")
                .Select(t => (t.Text, t.TextWrapping)).ToArray();
        });

        var title = Assert.Single(lines);
        Assert.Equal(TextWrapping.Wrap, title.TextWrapping);
    }

    private static byte[] Png()
    {
        var bitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, new byte[4 * 4 * 4], 4 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static T? Find<T>(DependencyObject root)
        where T : DependencyObject => Descendants(root).OfType<T>().FirstOrDefault();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static async Task<bool> WaitAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(5))
            {
                return false;
            }

            await Task.Delay(20, Ct);
        }

        return true;
    }

    private sealed class ScriptedMedia : IMediaSessionService
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public MediaInfo? Current { get; init; }

        public byte[]? Thumbnail { get; init; }

        public List<MediaCommand> Commands { get; } = [];

        public Task<bool> StartAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<MediaInfo?> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

        public Task<byte[]?> GetThumbnailAsync(int maxBytes, CancellationToken cancellationToken) => Task.FromResult(Thumbnail);

        public Task<bool> SendAsync(MediaCommand command, CancellationToken cancellationToken)
        {
            lock (Commands)
            {
                Commands.Add(command);
            }

            return Task.FromResult(true);
        }

        public void Dispose()
        {
        }
    }

    private sealed class Settings : ISettingsService
    {
        public event EventHandler<AppSettings>? Changed
        {
            add { }
            remove { }
        }

        public AppSettings Current { get; } = new();

        public bool IsReadOnly => false;

        public Task<SettingsSaveResult> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class States : IIslandStateMachine
    {
        public event EventHandler<IslandStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public IslandState State => IslandState.Expanded;

        public void Fire(IslandInput input) => throw new NotSupportedException();
    }
}
