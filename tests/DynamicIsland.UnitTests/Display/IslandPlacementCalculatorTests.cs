using DynamicIsland.Core.Display;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.UnitTests.Display;

public sealed class IslandPlacementCalculatorTests
{
    // 1920x1080 primary at 100 % with a 48 px bottom taskbar.
    private static readonly MonitorInfo Primary = new("primary", new(0, 0, 1920, 1080), new(0, 0, 1920, 1032), 1.0, IsPrimary: true);

    // 2560x1440 at 150 % to the LEFT of the primary (negative coordinates) with a top taskbar (work area starts lower).
    private static readonly MonitorInfo LeftHighDpi = new("left", new(-2560, 0, 2560, 1440), new(-2560, 60, 2560, 1380), 1.5, IsPrimary: false);

    private static readonly MonitorInfo[] TwoMonitors = [Primary, LeftHighDpi];

    [Fact]
    public void Default_is_top_center_of_the_primary_work_area()
    {
        var result = IslandPlacementCalculator.Place([Primary], Request());

        Assert.Equal(new PixelRect(860, 8, 200, 36), result.Rect);
        Assert.Same(Primary, result.Monitor);
        Assert.False(result.UsedFallback);
    }

    [Fact]
    public void Preferred_monitor_is_used_with_its_own_dpi_and_work_area()
    {
        var result = IslandPlacementCalculator.Place(TwoMonitors, Request(preferred: "LEFT"));

        // 200x36 DIP at 150 % = 300x54 px; centered in [-2560, 0); top = work area top (60) + 8 DIP * 1.5.
        Assert.Equal(new PixelRect(-1430, 72, 300, 54), result.Rect);
        Assert.Same(LeftHighDpi, result.Monitor);
    }

    [Fact]
    public void Missing_preferred_monitor_falls_back_to_primary_and_reports_it()
    {
        var result = IslandPlacementCalculator.Place(TwoMonitors, Request(preferred: "unplugged"));

        Assert.Same(Primary, result.Monitor);
        Assert.True(result.UsedFallback);
    }

    [Fact]
    public void Without_a_primary_flag_the_first_monitor_is_used()
    {
        var notPrimary = Primary with { IsPrimary = false };

        Assert.Same(notPrimary, IslandPlacementCalculator.Place([notPrimary, LeftHighDpi], Request()).Monitor);
    }

    [Theory]
    [InlineData(IslandPlacement.TopLeft, 20)]
    [InlineData(IslandPlacement.TopRight, 1920 - 200 - 20)]
    [InlineData(IslandPlacement.TopCenter, 860 + 20)]
    public void Placement_and_horizontal_offset(IslandPlacement placement, int expectedLeft)
    {
        var result = IslandPlacementCalculator.Place([Primary], Request(placement: placement, horizontal: 20));

        Assert.Equal(expectedLeft, result.Rect.Left);
    }

    [Theory]
    [InlineData(-5000, 0)]
    [InlineData(5000, 1920 - 200)]
    public void Island_is_clamped_inside_the_work_area(double horizontal, int expectedLeft)
    {
        var result = IslandPlacementCalculator.Place([Primary], Request(horizontal: horizontal, vertical: 5000));

        Assert.Equal(expectedLeft, result.Rect.Left);
        Assert.Equal(1032 - 36, result.Rect.Top);
        Assert.True(Primary.WorkArea.Contains(result.Rect));
    }

    [Fact]
    public void Island_never_covers_a_top_taskbar()
    {
        var result = IslandPlacementCalculator.Place([LeftHighDpi], Request(vertical: 0));

        Assert.Equal(LeftHighDpi.WorkArea.Top, result.Rect.Top);
    }

    [Fact]
    public void Oversized_island_is_shrunk_to_the_work_area()
    {
        var tiny = Primary with { WorkArea = new(0, 0, 150, 30) };

        var result = IslandPlacementCalculator.Place([tiny], Request());

        Assert.Equal(new PixelRect(0, 0, 150, 30), result.Rect);
    }

    [Fact]
    public void No_monitors_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => IslandPlacementCalculator.Place([], Request()));
    }

    private static PlacementRequest Request(
        string? preferred = null,
        IslandPlacement placement = IslandPlacement.TopCenter,
        double horizontal = 0,
        double vertical = 8) =>
        new(preferred, placement, horizontal, vertical, WidthDip: 200, HeightDip: 36);
}
