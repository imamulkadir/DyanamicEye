using System.Windows;
using DynamicIsland.App;

namespace DynamicIsland.IntegrationTests;

/// <summary>The island is drawn as a notch: only its outline is filled (and therefore clickable).</summary>
public sealed class NotchGeometryTests
{
    private const double Width = 236;
    private const double Height = 32;
    private const double Shoulder = 6;
    private const double BottomRadius = 16;

    [Theory]
    [InlineData(118, 16, true)] // body center
    [InlineData(118, 0.5, true)] // flat top edge, flush with the screen
    [InlineData(3, 0.5, true)] // shoulder flare at the very top-left
    [InlineData(1, 5, false)] // inside the concave top-left shoulder curve
    [InlineData(235, 5, false)] // inside the concave top-right shoulder curve
    [InlineData(7, 31, false)] // outside the rounded bottom-left corner
    [InlineData(229, 31, false)] // outside the rounded bottom-right corner
    [InlineData(118, 31.5, true)] // bottom edge center
    public void Notch_outline(double x, double y, bool inside)
    {
        var geometry = IslandSurface.CreateNotchGeometry(Width, Height, Shoulder, BottomRadius);

        Assert.Equal(inside, geometry.FillContains(new Point(x, y)));
    }

    [Fact]
    public void Radii_are_clamped_for_tiny_sizes()
    {
        var geometry = IslandSurface.CreateNotchGeometry(10, 4, Shoulder, BottomRadius);

        Assert.True(geometry.Bounds.Width <= 10 + 0.001);
        Assert.True(geometry.Bounds.Height <= 4 + 0.001);
    }
}
