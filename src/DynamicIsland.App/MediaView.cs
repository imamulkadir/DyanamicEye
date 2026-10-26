using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Core.Media;
using DynamicIsland.Core.Settings;
using DynamicIsland.Widgets;

namespace DynamicIsland.App;

/// <summary>
/// Expanded view of the media widget (ADR 0020): album art, title and artist, and previous / play-pause / next.
/// Buttons the player does not support are disabled. Keyboard: Tab reaches each button; names and tooltips describe them.
/// </summary>
internal static class MediaView
{
    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

    public static UIElement Create(MediaSnapshot media, WidgetPresenter presenter)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var art = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2B)),
            Child = presenter.AlbumArt.Get(media.ArtKey, media.Art) is { } image
                ? new Image { Source = image, Stretch = Stretch.UniformToFill }
                : new TextBlock { Text = "", FontFamily = new FontFamily(IconFont), FontSize = 18, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        art.Clip = new RectangleGeometry(new Rect(0, 0, 44, 44), 8, 8);
        AutomationProperties.SetName(art, "Album art");
        grid.Children.Add(art);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        // Many files have no artist tag (e.g. downloaded videos): the title may then use two lines instead.
        var detail = string.Join(" · ", new[] { media.Artist, media.OtherSessions > 0 ? $"{media.OtherSessions} more playing" : string.Empty }.Where(s => s.Length > 0));
        var title = Line(media.Title, 14, FontWeights.SemiBold, 0xFF);
        if (detail.Length == 0)
        {
            title.TextWrapping = TextWrapping.Wrap;
            title.MaxHeight = 40; // Two lines.
        }

        text.Children.Add(title);
        if (detail.Length > 0)
        {
            text.Children.Add(Line(detail, 12, FontWeights.Normal, 0xB3));
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var playing = media.State == MediaPlaybackState.Playing;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(Button("", "Previous track", MediaWidget.PreviousCommand, media.Controls.HasFlag(MediaControls.Previous), presenter));
        buttons.Children.Add(Button(
            playing ? "" : "",
            playing ? "Pause" : "Play",
            MediaWidget.PlayPauseCommand,
            media.Controls.HasFlag(playing ? MediaControls.Pause : MediaControls.Play),
            presenter));
        buttons.Children.Add(Button("", "Next track", MediaWidget.NextCommand, media.Controls.HasFlag(MediaControls.Next), presenter));
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        AutomationProperties.SetName(grid, $"Now playing: {media.Title}, {media.Artist}");
        return grid;
    }

    private static TextBlock Line(string text, double size, FontWeight weight, byte alpha) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        Foreground = new SolidColorBrush(Color.FromArgb(alpha, 0xF2, 0xF3, 0xF5)),
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static Button Button(string glyph, string name, string command, bool enabled, WidgetPresenter presenter)
    {
        var button = new Button
        {
            Content = glyph,
            FontFamily = new FontFamily(IconFont),
            FontSize = 14,
            Width = 32,
            Height = 32,
            Margin = new Thickness(2, 0, 2, 0),
            IsEnabled = enabled,
            // The player decides per track (e.g. no "previous" on the first track of its queue), so this is "right now",
            // not a statement about the player in general.
            ToolTip = enabled ? name : $"{name} (not available right now)",
            Style = IconButtonStyle,
        };
        ToolTipService.SetShowOnDisabled(button, true);
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetAutomationId(button, "Media." + command);
        button.Click += (_, _) => presenter.Execute(WidgetIds.Media, command);
        return button;
    }

    /// <summary>Dark, minimal icon button with a visible keyboard focus ring (matches the notch's pin button).</summary>
    private static readonly Style IconButtonStyle = CreateIconButtonStyle();

    private static Style CreateIconButtonStyle()
    {
        var chrome = new FrameworkElementFactory(typeof(Border), "Chrome");
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        chrome.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        chrome.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        chrome.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        chrome.AppendChild(content);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)), "Chrome"));
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5)), "Chrome"));
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.35));
        template.Triggers.Add(hover);
        template.Triggers.Add(focus);
        template.Triggers.Add(disabled);

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5))));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Seal();
        return style;
    }
}
