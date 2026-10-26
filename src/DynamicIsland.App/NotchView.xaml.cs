using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DynamicIsland.Core.Shell;

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
