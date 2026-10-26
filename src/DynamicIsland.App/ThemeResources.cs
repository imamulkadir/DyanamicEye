using System.Windows;
using System.Windows.Media;
using DynamicIsland.Core.Shell;

namespace DynamicIsland.App;

/// <summary>
/// Brushes for the island's color tokens (ADR 0026), kept in <see cref="NotchView"/>'s resources. Elements refer to them
/// by key (<c>DynamicResource</c> in XAML, <see cref="Themed{T}"/> in code), so replacing the brushes restyles every
/// widget in place without rebuilding views.
/// </summary>
public static class ThemeResources
{
    public const string Surface = "Island.Surface";
    public const string TextPrimary = "Island.TextPrimary";
    public const string TextSecondary = "Island.TextSecondary";
    public const string Accent = "Island.Accent";
    public const string AccentFill = "Island.AccentFill";
    public const string HoverFill = "Island.HoverFill";
    public const string FieldFill = "Island.FieldFill";
    public const string FocusRing = "Island.FocusRing";
    public const string Placeholder = "Island.Placeholder";

    /// <summary>Replaces the token brushes in <paramref name="resources"/>.</summary>
    public static void Apply(ResourceDictionary resources, IslandPalette palette)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(palette);

        resources[Surface] = Brush(palette.Surface);
        resources[TextPrimary] = Brush(palette.TextPrimary);
        resources[TextSecondary] = Brush(palette.TextSecondary);
        resources[Accent] = Brush(palette.Accent);
        resources[AccentFill] = Brush(palette.AccentFill);
        resources[HoverFill] = Brush(palette.HoverFill);
        resources[FieldFill] = Brush(palette.FieldFill);
        resources[FocusRing] = Brush(palette.FocusRing);
        resources[Placeholder] = Brush(palette.Placeholder);
    }

    /// <summary>Binds <paramref name="property"/> of <paramref name="element"/> to a token brush.</summary>
    public static T Themed<T>(this T element, DependencyProperty property, string key)
        where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }

    public static SolidColorBrush Brush(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();
        return brush;
    }
}
