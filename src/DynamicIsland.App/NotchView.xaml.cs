using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using DynamicIsland.Core.Widgets;

namespace DynamicIsland.App;

/// <summary>
/// The island's visuals (notch, clock, expanded panel). Pure presentation: the window sizes it and forwards
/// input; <see cref="ApplyVisualState"/> sets everything that depends on the state, so it can also be rendered offscreen.
/// </summary>
public partial class NotchView : UserControl
{
    private bool _expanded;
    private bool _clockInPanel;

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
        UpdateClockVisibility();
    }

    /// <summary>
    /// Shows the enabled widgets in order (ADR 0018). A failed widget gets a short note; a widget whose
    /// <paramref name="view"/> returns <c>null</c> (no content yet) takes no space.
    /// </summary>
    public void ShowWidgets(IReadOnlyList<WidgetSlot> slots, Func<WidgetSlot, UIElement?> view)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(view);

        var elements = new List<UIElement>(slots.Count);
        foreach (var slot in slots)
        {
            var element = slot.IsUnavailable ? UnavailableNote(slot) : view(slot);
            if (element is not null)
            {
                AutomationProperties.SetAutomationId(element, "Widget." + slot.Id);
                elements.Add(element);
            }
        }

        // Re-parent only when the visible widgets or their order changed, so focus and drags survive updates (ADR 0022).
        if (!elements.SequenceEqual(WidgetPanel.Children.OfType<UIElement>()))
        {
            WidgetPanel.Children.Clear();
            foreach (var element in elements)
            {
                WidgetPanel.Children.Add(element);
            }
        }

        WidgetPlaceholder.Visibility = WidgetPanel.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _clockInPanel = WidgetPanel.Children.OfType<UIElement>().Any(e => AutomationProperties.GetAutomationId(e) == "Widget." + WidgetIds.Clock);
        UpdateClockVisibility();
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised after <see cref="ShowWidgets"/>: the expanded content (and so its height) may have changed.</summary>
    public event EventHandler? ContentChanged;

    /// <summary>
    /// The height the expanded notch needs for its current content at <paramref name="width"/> DIPs, before clamping
    /// (ADR 0022). Call in an expanded state (after <see cref="ApplyVisualState"/>).
    /// </summary>
    public double MeasureExpandedHeight(double width)
    {
        // Settle pending layout first (changed texts or children only mark themselves dirty), then measure the panel
        // without a height limit; the next layout pass measures it again with the real notch height.
        UpdateLayout();
        ExpandedPanel.InvalidateMeasure();
        ExpandedPanel.Measure(new Size(width, double.PositiveInfinity));
        return ClockRow.Height.Value + ExpandedPanel.DesiredSize.Height;
    }

    /// <summary>Widget elements currently shown (tests).</summary>
    public IReadOnlyList<UIElement> WidgetElements => [.. WidgetPanel.Children.OfType<UIElement>()];

    /// <summary>The top-row time hides while the expanded panel shows the large clock, so the time is never shown twice.</summary>
    private void UpdateClockVisibility() =>
        ClockText.Visibility = string.IsNullOrEmpty(ClockText.Text) || (_expanded && _clockInPanel) ? Visibility.Collapsed : Visibility.Visible;

    private readonly Dictionary<string, TextBlock> _notes = new(StringComparer.Ordinal);

    /// <summary>One cached note per widget, so an unchanged failure does not re-parent the panel.</summary>
    private TextBlock UnavailableNote(WidgetSlot slot)
    {
        if (!_notes.TryGetValue(slot.Id, out var note))
        {
            note = new TextBlock
            {
                Text = $"{slot.Label} isn't available right now.",
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xB3, 0xF2, 0xF3, 0xF5)),
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 2),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            _notes.Add(slot.Id, note);
        }

        return note;
    }

    /// <summary>The clock text as shown (tests).</summary>
    public string ClockDisplay => ClockText.Text;

    /// <summary>True when the top-row time is drawn (tests).</summary>
    public bool IsClockVisible => ClockText.Visibility == Visibility.Visible;

    /// <param name="height">Target height in DIPs (after the appearance scale).</param>
    /// <param name="scale">User appearance scale.</param>
    public void ApplyVisualState(IslandState state, double height, double scale)
    {
        ClockRow.Height = new GridLength(IslandLayout.Collapsed.Height * scale);
        ClockText.FontSize = ClockFontSize * scale;
        var expanded = state is IslandState.Expanded or IslandState.Pinned;
        _expanded = expanded;
        UpdateClockVisibility();
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
