using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DynamicIsland.Core.Media;
using DynamicIsland.Core.Settings;
using DynamicIsland.Widgets;

namespace DynamicIsland.App;

/// <summary>
/// Expanded view of the media widget (ADR 0020): album art, title and artist, and previous / play-pause / next.
/// Buttons the player turns off are disabled. Keyboard: Tab reaches each button; names and tooltips describe them.
/// Persistent (ADR 0022): a focused button keeps focus across track changes.
/// </summary>
internal sealed class MediaView : WidgetView
{
    public const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

    /// <summary>Dark, minimal icon button with a visible keyboard focus ring (matches the notch's pin button).</summary>
    public static readonly Style IconButtonStyle = CreateIconButtonStyle();

    private readonly WidgetPresenter _presenter;
    private readonly Grid _grid = new() { Margin = new Thickness(0, 4, 0, 4) };
    private readonly Border _art;
    private readonly TextBlock _placeholder = new()
    {
        Text = "",
        FontFamily = new FontFamily(IconFont),
        FontSize = 18,
        Foreground = Brushes.Gray,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly Image _image = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _title = Line(14, FontWeights.SemiBold, Primary);
    private readonly TextBlock _detail = Line(12, FontWeights.Normal, Secondary);
    private readonly Button _previous;
    private readonly Button _playPause;
    private readonly Button _next;

    public MediaView(WidgetPresenter presenter)
    {
        _presenter = presenter;
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _art = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2B)),
            Clip = new RectangleGeometry(new Rect(0, 0, 44, 44), 8, 8),
            Child = _placeholder,
        };
        AutomationProperties.SetName(_art, "Album art");
        _grid.Children.Add(_art);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_title);
        text.Children.Add(_detail);
        Grid.SetColumn(text, 1);
        _grid.Children.Add(text);

        _previous = IconButton(MediaWidget.PreviousCommand);
        _playPause = IconButton(MediaWidget.PlayPauseCommand);
        _next = IconButton(MediaWidget.NextCommand);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(_previous);
        buttons.Children.Add(_playPause);
        buttons.Children.Add(_next);
        Grid.SetColumn(buttons, 2);
        _grid.Children.Add(buttons);
    }

    public override UIElement Element => _grid;

    public override void Update(object snapshot)
    {
        var media = (MediaSnapshot)snapshot;

        var image = _presenter.AlbumArt.Get(media.ArtKey, media.Art);
        _image.Source = image;
        _art.Child = image is null ? _placeholder : _image;

        // Many files have no artist tag (e.g. downloaded videos): the title may then use two lines instead.
        var detail = string.Join(" · ", new[] { media.Artist, media.OtherSessions > 0 ? $"{media.OtherSessions} more playing" : string.Empty }.Where(s => s.Length > 0));
        _title.Text = media.Title;
        _title.TextWrapping = detail.Length == 0 ? TextWrapping.Wrap : TextWrapping.NoWrap;
        _title.MaxHeight = detail.Length == 0 ? 40 : double.PositiveInfinity; // Two lines.
        _detail.Text = detail;
        _detail.Visibility = detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        var playing = media.State == MediaPlaybackState.Playing;
        SetButton(_previous, "", "Previous track", media.Controls.HasFlag(MediaControls.Previous));
        SetButton(_playPause, playing ? "" : "", playing ? "Pause" : "Play", media.Controls.HasFlag(playing ? MediaControls.Pause : MediaControls.Play));
        SetButton(_next, "", "Next track", media.Controls.HasFlag(MediaControls.Next));

        AutomationProperties.SetName(_grid, $"Now playing: {media.Title}, {media.Artist}");
    }

    private static TextBlock Line(double size, FontWeight weight, Brush brush) => new()
    {
        FontSize = size,
        FontWeight = weight,
        Foreground = brush,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static void SetButton(Button button, string glyph, string name, bool enabled)
    {
        button.Content = glyph;
        button.IsEnabled = enabled;

        // The player decides per track (e.g. no "previous" on the first track of its queue), so this is "right now",
        // not a statement about the player in general.
        button.ToolTip = enabled ? name : $"{name} (not available right now)";
        AutomationProperties.SetName(button, name);
    }

    private Button IconButton(string command)
    {
        var button = new Button
        {
            FontFamily = new FontFamily(IconFont),
            FontSize = 14,
            Width = 32,
            Height = 32,
            Margin = new Thickness(2, 0, 2, 0),
            Style = IconButtonStyle,
        };
        ToolTipService.SetShowOnDisabled(button, true);
        AutomationProperties.SetAutomationId(button, "Media." + command);
        button.Click += (_, _) => _presenter.Execute(WidgetIds.Media, command);
        return button;
    }

    private static Style CreateIconButtonStyle()
    {
        var chrome = new FrameworkElementFactory(typeof(Border), "Chrome");
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        chrome.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        chrome.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        chrome.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
        content.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        chrome.AppendChild(content);

        var template = new ControlTemplate(typeof(ButtonBase)) { VisualTree = chrome };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)), "Chrome"));
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5)), "Chrome"));
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.35));
        template.Triggers.Add(hover);
        template.Triggers.Add(focus);
        template.Triggers.Add(disabled);

        var style = new Style(typeof(ButtonBase));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5))));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Seal();
        return style;
    }
}
