using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Widgets;

namespace DynamicIsland.App;

/// <summary>Expanded clock (ADR 0019): the time in large light type with the full date below.</summary>
internal sealed class ClockView : WidgetView
{
    private readonly StackPanel _panel = new() { Margin = new Thickness(0, 2, 0, 6) };
    private readonly TextBlock _time = new TextBlock
    {
        FontSize = 34,
        FontWeight = FontWeights.Light,
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
    }.Themed(TextBlock.ForegroundProperty, ThemeResources.TextPrimary);

    private readonly TextBlock _date = new TextBlock { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis }
        .Themed(TextBlock.ForegroundProperty, ThemeResources.TextSecondary);

    public ClockView()
    {
        _panel.Children.Add(_time);
        _panel.Children.Add(_date);
    }

    public override UIElement Element => _panel;

    public override void Update(object snapshot)
    {
        var clock = (ClockSnapshot)snapshot;
        _time.Text = clock.Time;
        _date.Text = clock.Date;
        AutomationProperties.SetName(_panel, $"{clock.Time}, {clock.Date}");
    }
}
