using DynamicIsland.Core.Display;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.UnitTests.Display;

public sealed class FullscreenPolicyTests
{
    private const string IslandMonitor = "monitor-a";
    private static readonly BehaviorSettings Defaults = new();

    private static readonly FullscreenSnapshot VideoOnIslandMonitor =
        new(true, FullscreenSource.WindowCoversMonitor, IslandMonitor, "vlc.exe");

    [Fact]
    public void Fullscreen_app_on_the_island_monitor_hides_the_island()
    {
        Assert.True(FullscreenPolicy.ShouldHide(VideoOnIslandMonitor, IslandMonitor, Defaults));
    }

    [Fact]
    public void Monitor_ids_compare_case_insensitively()
    {
        Assert.True(FullscreenPolicy.ShouldHide(VideoOnIslandMonitor, "MONITOR-A", Defaults));
    }

    [Fact]
    public void Fullscreen_app_on_another_monitor_keeps_the_island_visible()
    {
        Assert.False(FullscreenPolicy.ShouldHide(VideoOnIslandMonitor with { MonitorId = "monitor-b" }, IslandMonitor, Defaults));
    }

    [Fact]
    public void Exclusive_fullscreen_without_a_window_monitor_hides_the_island()
    {
        var game = new FullscreenSnapshot(true, FullscreenSource.Direct3DFullscreen, null, null);

        Assert.True(FullscreenPolicy.ShouldHide(game, IslandMonitor, Defaults));
    }

    [Fact]
    public void Nothing_fullscreen_never_hides()
    {
        Assert.False(FullscreenPolicy.ShouldHide(FullscreenSnapshot.None, IslandMonitor, Defaults));
    }

    [Fact]
    public void Auto_hide_turned_off_never_hides()
    {
        Assert.False(FullscreenPolicy.ShouldHide(VideoOnIslandMonitor, IslandMonitor, Defaults with { AutoHideInFullscreen = false }));
    }

    [Theory]
    [InlineData("vlc.exe")]
    [InlineData("VLC.EXE")]
    public void Listed_exception_keeps_the_island_visible(string exception)
    {
        Assert.False(FullscreenPolicy.ShouldHide(VideoOnIslandMonitor, IslandMonitor, Defaults with { FullscreenExceptions = [exception] }));
    }

    [Fact]
    public void Exceptions_do_not_apply_when_the_app_is_unknown()
    {
        var unknownApp = VideoOnIslandMonitor with { ProcessExecutable = null };

        Assert.True(FullscreenPolicy.ShouldHide(unknownApp, IslandMonitor, Defaults with { FullscreenExceptions = ["vlc.exe"] }));
    }
}

public sealed class FullscreenExceptionValidationTests
{
    [Fact]
    public void Exceptions_are_trimmed_lower_cased_and_deduplicated()
    {
        var settings = new AppSettings { Behavior = new BehaviorSettings { FullscreenExceptions = ["  VLC.exe ", "vlc.exe", "mpv.exe"] } };

        var result = SettingsValidator.Normalize(settings);

        Assert.Equal(["vlc.exe", "mpv.exe"], result.Settings.Behavior.FullscreenExceptions);
        Assert.Contains("behavior.fullscreenExceptions: duplicate entry removed", result.Corrections);
    }

    [Theory]
    [InlineData(@"C:\Program Files\VideoLAN\vlc.exe")]
    [InlineData("notepad")]
    [InlineData("bad|name.exe")]
    [InlineData("")]
    public void Paths_and_invalid_names_are_rejected(string entry)
    {
        var settings = new AppSettings { Behavior = new BehaviorSettings { FullscreenExceptions = [entry] } };

        var result = SettingsValidator.Normalize(settings);

        Assert.Empty(result.Settings.Behavior.FullscreenExceptions);
        Assert.Single(result.Corrections);
    }

    [Fact]
    public void List_is_capped()
    {
        var many = Enumerable.Range(0, SettingsValidator.MaxFullscreenExceptions + 10).Select(i => $"app{i}.exe").ToArray();
        var settings = new AppSettings { Behavior = new BehaviorSettings { FullscreenExceptions = many } };

        var result = SettingsValidator.Normalize(settings);

        Assert.Equal(SettingsValidator.MaxFullscreenExceptions, result.Settings.Behavior.FullscreenExceptions.Count);
    }

    [Fact]
    public void Behavior_equality_compares_exception_contents()
    {
        Assert.Equal(new BehaviorSettings { FullscreenExceptions = ["vlc.exe"] }, new BehaviorSettings { FullscreenExceptions = ["vlc.exe"] });
        Assert.NotEqual(new BehaviorSettings { FullscreenExceptions = ["vlc.exe"] }, new BehaviorSettings());
    }
}
