using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DynamicIsland.Core.Metrics;
using DynamicIsland.Widgets;

namespace DynamicIsland.App;

/// <summary>Expanded view of the system metrics widget (ADR 0021): one row of small tiles; battery only when present.</summary>
internal sealed class MetricsView : WidgetView
{
    private readonly UniformGrid _row = new() { Rows = 1, Margin = new Thickness(0, 6, 0, 2) };
    private readonly Tile _cpu = new("CPU");
    private readonly Tile _memory = new("Memory");
    private readonly Tile _battery = new("Battery");
    private readonly Tile _uptime = new("Up time");

    public MetricsView()
    {
        _row.Children.Add(_cpu.Panel);
        _row.Children.Add(_memory.Panel);
        _row.Children.Add(_battery.Panel);
        _row.Children.Add(_uptime.Panel);
        AutomationProperties.SetName(_row, "System stats");
    }

    public override UIElement Element => _row;

    public override void Update(object snapshot)
    {
        var metrics = (MetricsSnapshot)snapshot;
        var culture = CultureInfo.CurrentCulture;
        _cpu.Set(MetricsFormat.Percent(metrics.CpuPercent, culture), "Processor");
        _memory.Set(MetricsFormat.Memory(metrics.Memory, culture), "Memory used");

        if (metrics.Battery is { HasBattery: true } battery)
        {
            _battery.Set(MetricsFormat.Percent(battery.Percent, culture) + (battery.Charging ? " ⚡" : string.Empty), battery.Charging ? "Battery, charging" : "Battery");
            _battery.Panel.Visibility = Visibility.Visible;
        }
        else
        {
            _battery.Panel.Visibility = Visibility.Collapsed;
        }

        _uptime.Set(MetricsFormat.Uptime(metrics.Uptime), "Time since Windows started");
        _row.Columns = _battery.Panel.Visibility == Visibility.Visible ? 4 : 3;
    }

    private sealed class Tile
    {
        private readonly TextBlock _value = new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        }.Themed(TextBlock.ForegroundProperty, ThemeResources.TextPrimary);

        public Tile(string label)
        {
            Panel.Children.Add(new TextBlock { Text = label, FontSize = 11 }.Themed(TextBlock.ForegroundProperty, ThemeResources.TextSecondary));
            Panel.Children.Add(_value);
        }

        public StackPanel Panel { get; } = new() { Margin = new Thickness(0, 0, 8, 0) };

        public void Set(string value, string spokenLabel)
        {
            _value.Text = value;
            AutomationProperties.SetName(Panel, $"{spokenLabel}: {value}");
        }
    }
}
