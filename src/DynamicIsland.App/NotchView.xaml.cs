using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DynamicIsland.Core.Shell;
using DynamicIsland.Core.Widgets;

namespace DynamicIsland.App;

/// <summary>
/// The island's visuals (notch, clock, expanded panel). Pure presentation: the window sizes it and forwards
/// input; <see cref="ApplyVisualState"/> sets everything that depends on the state, so it can also be rendered offscreen.
/// </summary>
public partial class NotchView : UserControl
{
    public NotchView() => InitializeComponent();

    /// <summary>The interactive notch surface (input, focus, UI Automation).</summary>
    public IslandSurface IslandSurface => Surface;

    public ToggleButton PinToggle => PinButton;

    /// <summary>Clock text at 100 % scale (DIPs).</summary>
    public const double ClockFontSize = 13;

    /// <summary>Shows the clock text; <c>null</c> or empty hides it (clock widget turned off).</summary>
    public void SetClock(string? text)
    {
        ClockText.Text = text ?? string.Empty;
        ClockText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Shows the enabled widgets in order (ADR 0018). A failed widget gets a short note; a widget whose
    /// <paramref name="view"/> returns <c>null</c> (no content yet) takes no space.
    /// </summary>
    public void ShowWidgets(IReadOnlyList<WidgetSlot> slots, Func<WidgetSlot, UIElement?> view)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(view);

        WidgetPanel.Children.Clear();
        foreach (var slot in slots)
        {
            var element = slot.IsUnavailable ? UnavailableNote(slot) : view(slot);
            if (element is not null)
            {
                AutomationProperties.SetAutomationId(element, "Widget." + slot.Id);
                WidgetPanel.Children.Add(element);
            }
        }

        WidgetPlaceholder.Visibility = WidgetPanel.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Widget elements currently shown (tests).</summary>
    public IReadOnlyList<UIElement> WidgetElements => [.. WidgetPanel.Children.OfType<UIElement>()];

    private static TextBlock UnavailableNote(WidgetSlot slot) => new()
    {
        Text = $"{slot.Label} isn't available right now.",
        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xB3, 0xF2, 0xF3, 0xF5)),
        FontSize = 12,
        Margin = new Thickness(0, 2, 0, 2),
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>The clock text as shown (tests).</summary>
    public string ClockDisplay => ClockText.Visibility == Visibility.Visible ? ClockText.Text : string.Empty;

    /// <param name="height">Target height in DIPs (after the appearance scale).</param>
    /// <param name="scale">User appearance scale.</param>
    public void ApplyVisualState(IslandState state, double height, double scale)
    {
        ClockRow.Height = new GridLength(IslandLayout.Collapsed.Height * scale);
        ClockText.FontSize = ClockFontSize * scale;
        var expanded = state is IslandState.Expanded or IslandState.Pinned;
        Surface.BottomRadius = Math.Min(height / 2, 22 * scale);
        ExpandedPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        PinButton.IsChecked = state == IslandState.Pinned;
        AutomationProperties.SetName(Surface, state switch
        {
            IslandState.Peek => "Dynamic Island, preview",
            IslandState.Expanded => "Dynamic Island, expanded",
            IslandState.Pinned => "Dynamic Island, expanded and pinned",
            _ => "Dynamic Island, collapsed",
        });
    }
}
