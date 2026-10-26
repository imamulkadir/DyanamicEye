using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;

namespace DynamicIsland.UnitTests.Shell;

/// <summary>Color tokens and safe sizes (P05-01, ADR 0026).</summary>
public sealed class IslandThemeTests
{
    public static TheoryData<string> Palettes => ["Dark", "Light"];

    [Theory]
    [InlineData(ThemeMode.Dark, false, "Dark")]
    [InlineData(ThemeMode.Dark, true, "Dark")]
    [InlineData(ThemeMode.Light, false, "Light")]
    [InlineData(ThemeMode.System, true, "Light")]
    [InlineData(ThemeMode.System, false, "Dark")]
    public void Theme_setting_resolves_to_a_palette(ThemeMode mode, bool windowsLight, string expected) =>
        Assert.Equal(expected, IslandTheme.Resolve(mode, windowsLight).Name);

    [Theory]
    [MemberData(nameof(Palettes))]
    public void Text_and_accent_stay_readable_on_the_surface(string name)
    {
        var palette = name == "Dark" ? IslandTheme.Dark : IslandTheme.Light;

        // WCAG AA: 4.5:1 for text, 3:1 for the accent icon and the focus ring (non-text UI).
        Assert.True(IslandTheme.ContrastRatio(palette.TextPrimary, palette.Surface) >= 4.5, "primary text");
        Assert.True(IslandTheme.ContrastRatio(palette.TextSecondary, palette.Surface) >= 4.5, "secondary text");
        Assert.True(IslandTheme.ContrastRatio(palette.Accent, palette.Surface) >= 3, "accent");
        Assert.True(IslandTheme.ContrastRatio(palette.FocusRing, palette.Surface) >= 3, "focus ring");
        Assert.True(IslandTheme.ContrastRatio(palette.TextSecondary, palette.Placeholder) >= 3, "media placeholder icon");
    }

    [Fact]
    public void High_contrast_uses_only_the_users_system_colors_without_tints()
    {
        // "Aquatic"-like colors, given without alpha as Windows reports them.
        var palette = IslandTheme.HighContrast(window: 0x202020, windowText: 0xFFFFFF, highlight: 0x8EE3F0);

        Assert.Equal((0xFF202020u, 0xFFFFFFFFu, 0xFFFFFFFFu), (palette.Surface, palette.TextPrimary, palette.TextSecondary));
        Assert.Equal((0xFF8EE3F0u, 0xFF8EE3F0u), (palette.Accent, palette.FocusRing));
        Assert.All([palette.AccentFill, palette.HoverFill, palette.FieldFill], fill => Assert.Equal(0u, fill)); // No tints.
    }

    [Fact]
    public void Contrast_ratio_matches_the_wcag_reference_values()
    {
        Assert.Equal(21, IslandTheme.ContrastRatio(0xFFFFFFFF, 0xFF000000), 3);
        Assert.Equal(1, IslandTheme.ContrastRatio(0xFF777777, 0xFF777777), 3);
        Assert.Equal(1, IslandTheme.ContrastRatio(0x00FFFFFF, 0xFF000000), 3); // Fully transparent text is invisible.
    }

    [Theory]
    [InlineData(1.0, 1920, 1040, 1.0)] // Fits: unchanged.
    [InlineData(2.0, 3840, 2080, 2.0)]
    [InlineData(2.0, 1920, 1040, 1.7333)] // 360 × s must stay within 60 % of 1040.
    [InlineData(1.25, 1366, 728, 1.2133)]
    [InlineData(0.75, 1366, 728, 0.75)]
    [InlineData(1.0, 800, 560, 0.9333)]
    [InlineData(1.0, 300, 200, 0.5)] // Tiny screen: never below the readable minimum.
    [InlineData(0.4, 300, 200, 0.4)] // ...but never larger than asked.
    public void Safe_scale_fits_the_largest_island_into_the_work_area(double requested, double width, double height, double expected) =>
        Assert.Equal(expected, IslandLayout.SafeScale(requested, width, height), 4);

    [Theory]
    [InlineData(1.5, 0, 1000, 1.5)]
    [InlineData(1.5, double.NaN, 1000, 1.5)]
    [InlineData(double.NaN, 1920, 1040, 1.0)]
    [InlineData(-2, 1920, 1040, 1.0)]
    public void Safe_scale_ignores_unusable_input(double requested, double width, double height, double expected) =>
        Assert.Equal(expected, IslandLayout.SafeScale(requested, width, height), 4);
}
