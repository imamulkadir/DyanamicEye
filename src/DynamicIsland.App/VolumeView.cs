using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using DynamicIsland.Widgets;

namespace DynamicIsland.App;

/// <summary>
/// Expanded view of the volume widget (ADR 0022): mute toggle, a 0–100 slider (arrows ±2, Page Up/Down ±10, click
/// moves to the point) and the percentage. While the user drags or types, incoming updates do not move the slider;
/// changes are sent at most every <see cref="SendInterval"/> plus the final value.
/// </summary>
internal sealed class VolumeView : WidgetView
{
    public static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(50);

    private readonly WidgetPresenter _presenter;
    private readonly Grid _grid = new() { Margin = new Thickness(0, 6, 0, 2) };
    private readonly ToggleButton _mute;
    private readonly Slider _slider;
    private readonly TextBlock _percent = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, MinWidth = 38, TextAlignment = TextAlignment.Right }
        .Themed(TextBlock.ForegroundProperty, ThemeResources.TextPrimary);
    private readonly TextBlock _noDevice = new TextBlock { Text = "No audio output", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed }
        .Themed(TextBlock.ForegroundProperty, ThemeResources.TextSecondary);
    private readonly DispatcherTimer _sendTimer;
    private bool _applying;
    private int? _pending;
    private long _lastUserChange;

    /// <summary>After the last slider change by the user, echoes of older values are ignored for this long.</summary>
    private const long UserQuietMilliseconds = 400;

    public VolumeView(WidgetPresenter presenter)
    {
        _presenter = presenter;
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _mute = new ToggleButton
        {
            FontFamily = new FontFamily(MediaView.IconFont),
            FontSize = 15,
            Width = 32,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            Style = MediaView.IconButtonStyle,
        };
        AutomationProperties.SetAutomationId(_mute, "Volume.Mute");
        _mute.Click += (_, _) => _presenter.Execute(WidgetIds.Volume, VolumeWidget.MuteToggleCommand);
        _grid.Children.Add(_mute);

        _slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            SmallChange = 2,
            LargeChange = 10,
            IsMoveToPointEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            MinHeight = IslandTheme.MinTarget,
        };
        AutomationProperties.SetName(_slider, "Volume");
        AutomationProperties.SetAutomationId(_slider, "Volume.Level");
        _slider.ValueChanged += OnSliderValueChanged;
        var sliderRing = ThemeResources.WithFocusRing(_slider);
        Grid.SetColumn(sliderRing, 1);
        _grid.Children.Add(sliderRing);
        Grid.SetColumn(_noDevice, 1);
        _grid.Children.Add(_noDevice);

        Grid.SetColumn(_percent, 2);
        _grid.Children.Add(_percent);

        _sendTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = SendInterval };
        _sendTimer.Tick += (_, _) => Flush();
    }

    public override UIElement Element => _grid;

    /// <summary>True while the user is changing the volume here (incoming updates must not fight the slider).</summary>
    private bool UserIsAdjusting =>
        _slider.IsMouseCaptureWithin || _pending is not null || Environment.TickCount64 - _lastUserChange < UserQuietMilliseconds;

    public override void Update(object snapshot)
    {
        var volume = (VolumeSnapshot)snapshot;

        _slider.Visibility = volume.HasDevice ? Visibility.Visible : Visibility.Collapsed;
        _noDevice.Visibility = volume.HasDevice ? Visibility.Collapsed : Visibility.Visible;
        _mute.IsEnabled = volume.HasDevice;
        _mute.IsChecked = volume.Muted;
        _mute.Content = volume.Muted ? "" : volume.Percent == 0 ? "" : volume.Percent < 34 ? "" : volume.Percent < 67 ? "" : "";
        var muteName = volume.Muted ? "Unmute" : "Mute";
        _mute.ToolTip = muteName;
        AutomationProperties.SetName(_mute, muteName);

        if (!UserIsAdjusting)
        {
            _applying = true;
            _slider.Value = volume.Percent;
            _applying = false;
        }

        _percent.Text = volume.HasDevice ? volume.Percent.ToString(CultureInfo.CurrentCulture) + "%" : string.Empty;
        AutomationProperties.SetName(_grid, volume.HasDevice ? $"Volume {volume.Percent}%{(volume.Muted ? ", muted" : string.Empty)}" : "No audio output");
    }

    private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_applying)
        {
            return;
        }

        _lastUserChange = Environment.TickCount64;
        _pending = (int)Math.Round(e.NewValue);
        _percent.Text = _pending.Value.ToString(CultureInfo.CurrentCulture) + "%";
        if (!_sendTimer.IsEnabled)
        {
            Flush();
            _sendTimer.Start();
        }
    }

    /// <summary>Sends the latest pending value; the timer stops once nothing is pending.</summary>
    private void Flush()
    {
        if (_pending is not { } percent)
        {
            _sendTimer.Stop();
            return;
        }

        _pending = null;
        _presenter.Execute(WidgetIds.Volume, VolumeWidget.SetCommand(percent));
    }
}
