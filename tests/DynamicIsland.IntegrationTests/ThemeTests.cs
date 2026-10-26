using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.App;
using DynamicIsland.Core.Components;
using DynamicIsland.Core.Shell;
using DynamicIsland.Core.Widgets;
using DynamicIsland.TestSupport;

namespace DynamicIsland.IntegrationTests;

/// <summary>Switching the palette restyles the notch and widget views in place (P05-01, ADR 0026).</summary>
public sealed class ThemeTests : IDisposable
{
    private readonly StaDispatcherThread _ui = new();

    public void Dispose() => _ui.Dispose();

    [Fact]
    public void Light_and_dark_palettes_restyle_the_notch_and_existing_widget_views()
    {
        var (dark, light) = _ui.Invoke(() =>
        {
            var view = new NotchView();
            var text = new TextBlock { Text = "Song" }.Themed(TextBlock.ForegroundProperty, ThemeResources.TextPrimary);
            view.ShowWidgets([new WidgetSlot("media", "Media", new ComponentStatus(WidgetRegistry.ComponentName("media"), ComponentState.Healthy), "Song")], _ => text);

            var before = (Fill(view.IslandSurface.Fill), Fill(text.Foreground));
            view.ApplyTheme(IslandTheme.Light);
            return (before, (Fill(view.IslandSurface.Fill), Fill(text.Foreground)));
        });

        Assert.Equal((IslandTheme.Dark.Surface, IslandTheme.Dark.TextPrimary), dark);
        Assert.Equal((IslandTheme.Light.Surface, IslandTheme.Light.TextPrimary), light);
    }

    private static uint Fill(Brush brush)
    {
        var color = Assert.IsType<SolidColorBrush>(brush).Color;
        return ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
    }
}
