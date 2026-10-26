using DynamicIsland.Core.Settings;

namespace DynamicIsland.Core.Shell;

/// <summary>
/// The island's color tokens (ADR 0026), as ARGB values. Views use the tokens by role, never raw colors, so a theme
/// change restyles everything in place. Text roles meet WCAG AA (4.5:1) and the accent 3:1 against the surface.
/// </summary>
public sealed record IslandPalette(
    string Name,
    uint Surface,
    uint TextPrimary,
    uint TextSecondary,
    uint Accent,
    uint AccentFill,
    uint HoverFill,
    uint FieldFill,
    uint FocusRing,
    uint Placeholder);

public static class IslandTheme
{
    /// <summary>The classic black notch.</summary>
    public static readonly IslandPalette Dark = new(
        "Dark",
        Surface: 0xFF000000,
        TextPrimary: 0xFFF2F3F5,
        TextSecondary: 0xB3F2F3F5,
        Accent: 0xFF3DDC84,
        AccentFill: 0x1F3DDC84,
        HoverFill: 0x26FFFFFF,
        FieldFill: 0x26FFFFFF,
        FocusRing: 0xFFF2F3F5,
        Placeholder: 0xFF26262B);

    /// <summary>A light island for light desktops; the accent is a darker green so it stays readable.</summary>
    public static readonly IslandPalette Light = new(
        "Light",
        Surface: 0xFFF3F3F5,
        TextPrimary: 0xFF1B1B1F,
        TextSecondary: 0xB31B1B1F,
        Accent: 0xFF0B7A3E,
        AccentFill: 0x1F0B7A3E,
        HoverFill: 0x14000000,
        FieldFill: 0x14000000,
        FocusRing: 0xFF1B1B1F,
        Placeholder: 0xFFDCDCE0);

    /// <summary>
    /// Windows high contrast (ADR 0028): only the user's system colors, all opaque (no tints or alpha), so the theme the
    /// user picked decides every color. Hover shows no fill; focus and selection use the highlight color.
    /// </summary>
    public static IslandPalette HighContrast(uint window, uint windowText, uint highlight) => new(
        "HighContrast",
        Surface: window | 0xFF000000,
        TextPrimary: windowText | 0xFF000000,
        TextSecondary: windowText | 0xFF000000,
        Accent: highlight | 0xFF000000,
        AccentFill: 0x00000000,
        HoverFill: 0x00000000,
        FieldFill: 0x00000000,
        FocusRing: highlight | 0xFF000000,
        Placeholder: window | 0xFF000000);

    /// <summary>Minimum size of any clickable control in the island, in DIPs at 100 % (WCAG 2.2 target size, ADR 0028).</summary>
    public const double MinTarget = 24;

    /// <summary>Minimum size of the island's main controls: tabs, pin, media and volume buttons.</summary>
    public const double MinPrimaryTarget = 32;

    /// <summary>Shape tokens in DIPs at 100 % (scaled with the island).</summary>
    public const double ShoulderRadius = 6;
    public const double BottomRadius = 16;
    public const double ControlRadius = 8;

    /// <summary>"Use Windows setting" follows the Windows app mode (light or dark).</summary>
    public static IslandPalette Resolve(ThemeMode mode, bool windowsUsesLightTheme) => mode switch
    {
        ThemeMode.Light => Light,
        ThemeMode.Dark => Dark,
        _ => windowsUsesLightTheme ? Light : Dark,
    };

    /// <summary>WCAG 2 contrast ratio of <paramref name="foreground"/> (alpha-blended) over an opaque <paramref name="background"/>.</summary>
    public static double ContrastRatio(uint foreground, uint background)
    {
        var alpha = (foreground >> 24) / 255.0;
        double Blend(int shift) => (alpha * Channel(foreground, shift)) + ((1 - alpha) * Channel(background, shift));

        var front = Luminance(Blend(16), Blend(8), Blend(0));
        var back = Luminance(Channel(background, 16), Channel(background, 8), Channel(background, 0));
        var (light, dark) = front > back ? (front, back) : (back, front);
        return (light + 0.05) / (dark + 0.05);
    }

    private static double Channel(uint argb, int shift) => ((argb >> shift) & 0xFF) / 255.0;

    private static double Luminance(double r, double g, double b) => (0.2126 * Linear(r)) + (0.7152 * Linear(g)) + (0.0722 * Linear(b));

    private static double Linear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
}
