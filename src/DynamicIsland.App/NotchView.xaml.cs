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
    private readonly Dictionary<string, UIElement> _elements = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RadioButton> _tabs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal);
    private bool _expanded;
    private bool _clockInPanel;
    private string? _selectedId;
    private string? _chosenId; // The user's pick; an automatic pick never sticks.
    private bool _selecting;

    public NotchView()
    {
        ThemeResources.Apply(Resources, IslandTheme.Dark);
        InitializeComponent();
    }

    /// <summary>The palette in use (ADR 0026).</summary>
    public IslandPalette Palette { get; private set; } = IslandTheme.Dark;

    /// <summary>Restyles the notch and every widget view in place (they refer to the token brushes by key).</summary>
    public void ApplyTheme(IslandPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (palette != Palette)
        {
            Palette = palette;
            ThemeResources.Apply(Resources, palette);
        }
    }

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
    /// Shows one icon tab per enabled widget that has something to show (a failed widget's tab shows a short note), in
    /// the user's order, and the selected widget's view below them (ADR 0025). Tabs, views and notes are reused, so focus
    /// and drags survive updates. A tab the user picked stays selected while its widget exists; otherwise the first tab is.
    /// </summary>
    public void ShowWidgets(IReadOnlyList<WidgetSlot> slots, Func<WidgetSlot, UIElement?> view)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(view);

        _elements.Clear();
        var ids = new List<string>(slots.Count);
        foreach (var slot in slots)
        {
            var element = slot.IsUnavailable ? UnavailableNote(slot) : view(slot);
            if (element is not null)
            {
                AutomationProperties.SetAutomationId(element, "Widget." + slot.Id);
                _elements[slot.Id] = element;
                ids.Add(slot.Id);
                _labels[slot.Id] = slot.Label;
            }
        }

        var tabs = ids.Select(Tab).ToArray();
        if (!tabs.SequenceEqual(TabStrip.Children.OfType<UIElement>()))
        {
            TabStrip.Children.Clear();
            foreach (var tab in tabs)
            {
                TabStrip.Children.Add(tab);
            }
        }

        // A single widget needs no tab strip.
        TabStrip.Visibility = ids.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (_chosenId is not null && !ids.Contains(_chosenId))
        {
            _chosenId = null;
        }

        Select(_chosenId ?? ids.FirstOrDefault(), force: true);
    }

    /// <summary>The widget whose view is shown, or <c>null</c> when none has content.</summary>
    public string? SelectedWidgetId => _selectedId;

    /// <summary>Raised when another widget's tab was selected (the presenter makes only that widget "expanded").</summary>
    public event EventHandler? SelectedWidgetChanged;

    /// <summary>Selects a widget's tab (user click, keyboard or tests); ignored for widgets without a tab.</summary>
    public void SelectWidget(string id)
    {
        if (_elements.ContainsKey(id))
        {
            Choose(id);
        }
    }

    /// <summary>Moves to the next (+1) or previous (-1) tab, wrapping around (Ctrl+Tab / Ctrl+Shift+Tab).</summary>
    public void SelectAdjacentWidget(int direction)
    {
        var ids = TabStrip.Children.OfType<FrameworkElement>().Select(t => (string)t.Tag).ToList();
        if (ids.Count < 2 || _selectedId is null)
        {
            return;
        }

        var next = ids[(ids.IndexOf(_selectedId) + direction + ids.Count) % ids.Count];
        Choose(next);
        _tabs[next].Focus();
    }

    private void Choose(string id)
    {
        _chosenId = id;
        Select(id, force: false);
    }

    private void Select(string? id, bool force)
    {
        var changed = id != _selectedId;
        if (!changed && !force)
        {
            return;
        }

        _selectedId = id;
        var element = id is null ? null : _elements[id];
        if (WidgetPanel.Children.Count != (element is null ? 0 : 1) || (element is not null && WidgetPanel.Children[0] != element))
        {
            WidgetPanel.Children.Clear();
            if (element is not null)
            {
                WidgetPanel.Children.Add(element);
            }
        }

        _selecting = true;
        try
        {
            foreach (var (tabId, tab) in _tabs)
            {
                tab.IsChecked = tabId == id;
            }
        }
        finally
        {
            _selecting = false;
        }

        WidgetPlaceholder.Visibility = element is null ? Visibility.Visible : Visibility.Collapsed;
        _clockInPanel = id == WidgetIds.Clock;
        UpdateClockVisibility();
        if (changed)
        {
            SelectedWidgetChanged?.Invoke(this, EventArgs.Empty);
        }

        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The icon tab for a widget (created once). Radio semantics: arrow keys move, the selected one is checked.</summary>
    private RadioButton Tab(string id)
    {
        if (_tabs.TryGetValue(id, out var tab))
        {
            return tab;
        }

        var label = _labels.GetValueOrDefault(id, id);
        tab = new RadioButton
        {
            Tag = id,
            GroupName = "IslandWidgetTabs",
            Content = TabGlyph(id),
            FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 15,
            Width = 34,
            Height = 30,
            Margin = new Thickness(0, 0, 4, 0),
            Style = (Style)Resources["IslandIconToggle"],
            ToolTip = label,
        };
        AutomationProperties.SetName(tab, label);
        AutomationProperties.SetAutomationId(tab, "Tab." + id);
        tab.Checked += (_, _) =>
        {
            if (!_selecting)
            {
                Choose(id);
            }
        };
        _tabs.Add(id, tab);
        return tab;
    }

    private static string TabGlyph(string id) => id switch
    {
        WidgetIds.Clock => "\uE823", // Clock
        WidgetIds.Media => "\uE8D6", // Music note
        WidgetIds.SystemStats => "\uE9D9", // Diagnostic
        WidgetIds.Volume => "\uE767", // Volume
        WidgetIds.Tasks => "\uE762", // Multi-select
        WidgetIds.Notes => "\uE70B", // Quick note
        _ => "\uE74C",
    };

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

    /// <summary>
    /// The expanded height for the selected tab at <paramref name="width"/> (scaled DIPs), clamped to the expanded range
    /// (ADR 0022). Only content taller than the maximum can scroll; otherwise scrolling is off, so no scroll bar flashes
    /// while the island grows to fit a taller tab.
    /// </summary>
    public double FitExpandedHeight(double width, double scale)
    {
        var content = MeasureExpandedHeight(width) / scale;
        WidgetScroll.VerticalScrollBarVisibility = content > IslandLayout.ExpandedMaxHeight ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        return IslandLayout.ExpandedHeightFor(content) * scale;
    }

    /// <summary>Whether the widget area can scroll (tests).</summary>
    public bool IsScrollingAllowed => WidgetScroll.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled;

    /// <summary>The selected widget's element, as shown (tests).</summary>
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
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 2),
                TextTrimming = TextTrimming.CharacterEllipsis,
            }.Themed(TextBlock.ForegroundProperty, ThemeResources.TextSecondary);
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
