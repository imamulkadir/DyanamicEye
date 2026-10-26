using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DynamicIsland.Core.Metrics;
using DynamicIsland.Widgets;

namespace DynamicIsland.App;

/// <summary>Expanded view of the system metrics widget (ADR 0021): one row of small tiles; battery only when present.</summary>
internal static class MetricsView
{
    public static UIElement Create(MetricsSnapshot metrics)
    {
        var culture = CultureInfo.CurrentCulture;
        var tiles = new List<UIElement>
        {
            Tile("CPU", MetricsFormat.Percent(metrics.CpuPercent, culture), "Processor"),
            Tile("Memory", MetricsFormat.Memory(metrics.Memory, culture), "Memory used"),
        };

        if (metrics.Battery is { HasBattery: true } battery)
        {
            var value = MetricsFormat.Percent(battery.Percent, culture) + (battery.Charging ? " ⚡" : string.Empty);
            tiles.Add(Tile("Battery", value, battery.Charging ? "Battery, charging" : "Battery"));
        }

        tiles.Add(Tile("Up time", MetricsFormat.Uptime(metrics.Uptime), "Time since Windows started"));

        var row = new UniformGrid { Rows = 1, Margin = new Thickness(0, 6, 0, 2) };
        foreach (var tile in tiles)
        {
            row.Children.Add(tile);
        }

        AutomationProperties.SetName(row, "System stats");
        return row;
    }

    private static StackPanel Tile(string label, string value, string spokenLabel)
    {
        var tile = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        tile.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xF2, 0xF3, 0xF5)) });
        tile.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5)),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        AutomationProperties.SetName(tile, $"{spokenLabel}: {value}");
        return tile;
    }
}
