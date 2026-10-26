using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using DynamicIsland.Core.Display;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Core.Shell;

public enum SettingsSection
{
    General,
    Appearance,
    Behavior,
    Widgets,
}

/// <summary>An entry of the display list; <see cref="Id"/> <c>null</c> means "always the primary display".</summary>
public sealed record DisplayOption(string? Id, string Label);

/// <summary>One built-in widget in the editable, ordered widget list.</summary>
public sealed class WidgetOption(string id, string label, bool enabled) : INotifyPropertyChanged
{
    private bool _enabled = enabled;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when <see cref="Enabled"/> changes, so the editor can update its dirty state.</summary>
    internal event EventHandler? EnabledChanged;

    public string Id { get; } = id;

    public string Label { get; } = label;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
            EnabledChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>
/// Draft of the settings window (ADR 0014): typed values are validated strictly with user-facing messages (the loader's
/// silent clamping is not used for input), Save is possible only without errors, and <see cref="ApplyTo"/> writes only
/// the fields the user changed so concurrent changes (e.g. a display picked from the tray) are kept. Consent settings
/// change only when the user ticks or clears their box (ADR 0015) and are never reset.
/// </summary>
public sealed class SettingsEditor : INotifyPropertyChanged
{
    private static readonly IReadOnlyDictionary<string, string> WidgetLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [WidgetIds.Clock] = "Clock and date",
        [WidgetIds.Media] = "Media",
        [WidgetIds.SystemStats] = "System stats",
        [WidgetIds.Volume] = "Volume",
        [WidgetIds.Notes] = "Notes",
        [WidgetIds.Tasks] = "Tasks",
    };

    private readonly CultureInfo _culture;
    private readonly IReadOnlyList<MonitorInfo> _monitors;

    private DisplayOption _selectedDisplay = null!;
    private ThemeMode _theme;
    private double _scale;
    private double _opacity;
    private IslandPlacement _placement;
    private string _horizontalOffsetText = string.Empty;
    private string _verticalOffsetText = string.Empty;
    private MotionPreference _motion;
    private ClockHourFormat _clockHourFormat;
    private bool _clockShowSeconds;
    private ClockDateFormat _clockDateFormat;
    private bool _hoverPreviewEnabled;
    private string _hoverPreviewDelayText = string.Empty;
    private bool _autoHideInFullscreen;
    private string _fullscreenExceptionsText = string.Empty;
    private string? _horizontalOffsetError;
    private string? _verticalOffsetError;
    private string? _hoverPreviewDelayError;
    private string? _fullscreenExceptionsError;
    private bool _loading;
    private bool _startWithWindows;
    private bool _dailyUpdateChecks;

    public SettingsEditor(AppSettings saved, IReadOnlyList<MonitorInfo> monitors, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(culture);

        _monitors = monitors;
        _culture = culture;
        Widgets.CollectionChanged += (_, _) => OnDraftChanged();
        Load(saved);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The settings this draft started from (the reference for <see cref="IsDirty"/> and <see cref="ApplyTo"/>).</summary>
    public AppSettings Saved { get; private set; } = null!;

    public IReadOnlyList<DisplayOption> DisplayOptions { get; private set; } = [];

    public ObservableCollection<WidgetOption> Widgets { get; } = [];

    /// <summary>Sign-in startup consent (ADR 0015). Untouched, an unanswered consent stays <see cref="ConsentState.NotAsked"/>; never reset.</summary>
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => Set(ref _startWithWindows, value);
    }

    /// <summary>Daily update check consent; same rules as <see cref="StartWithWindows"/>.</summary>
    public bool DailyUpdateChecks
    {
        get => _dailyUpdateChecks;
        set => Set(ref _dailyUpdateChecks, value);
    }

    public DisplayOption SelectedDisplay
    {
        get => _selectedDisplay;
        set => Set(ref _selectedDisplay, value ?? DisplayOptions[0]);
    }

    public ThemeMode Theme
    {
        get => _theme;
        set => Set(ref _theme, value);
    }

    /// <summary>Island size factor; rounded to 0.05 so a slider drag never produces noise values.</summary>
    public double Scale
    {
        get => _scale;
        set => Set(ref _scale, Math.Clamp(Math.Round(value * 20) / 20, SettingsValidator.MinScale, SettingsValidator.MaxScale));
    }

    public double Opacity
    {
        get => _opacity;
        set => Set(ref _opacity, Math.Clamp(Math.Round(value * 20) / 20, SettingsValidator.MinOpacity, SettingsValidator.MaxOpacity));
    }

    public IslandPlacement Placement
    {
        get => _placement;
        set => Set(ref _placement, value);
    }

    public string HorizontalOffsetText
    {
        get => _horizontalOffsetText;
        set => Set(ref _horizontalOffsetText, value ?? string.Empty);
    }

    public string VerticalOffsetText
    {
        get => _verticalOffsetText;
        set => Set(ref _verticalOffsetText, value ?? string.Empty);
    }

    public MotionPreference Motion
    {
        get => _motion;
        set => Set(ref _motion, value);
    }

    public ClockHourFormat ClockHourFormat
    {
        get => _clockHourFormat;
        set => Set(ref _clockHourFormat, value);
    }

    public bool ClockShowSeconds
    {
        get => _clockShowSeconds;
        set => Set(ref _clockShowSeconds, value);
    }

    public ClockDateFormat ClockDateFormat
    {
        get => _clockDateFormat;
        set => Set(ref _clockDateFormat, value);
    }

    /// <summary>The notch clock as the draft would show it at <paramref name="localTime"/>.</summary>
    public string ClockPreview(DateTime localTime) =>
        ClockFormatter.Format(localTime, new ClockSettings { HourFormat = _clockHourFormat, ShowSeconds = _clockShowSeconds, DateFormat = _clockDateFormat }, _culture);

    public bool HoverPreviewEnabled
    {
        get => _hoverPreviewEnabled;
        set => Set(ref _hoverPreviewEnabled, value);
    }

    public string HoverPreviewDelayText
    {
        get => _hoverPreviewDelayText;
        set => Set(ref _hoverPreviewDelayText, value ?? string.Empty);
    }

    public bool AutoHideInFullscreen
    {
        get => _autoHideInFullscreen;
        set => Set(ref _autoHideInFullscreen, value);
    }

    /// <summary>One program file name per line (e.g. <c>vlc.exe</c>); blank lines are ignored.</summary>
    public string FullscreenExceptionsText
    {
        get => _fullscreenExceptionsText;
        set => Set(ref _fullscreenExceptionsText, value ?? string.Empty);
    }

    public string? HorizontalOffsetError
    {
        get => _horizontalOffsetError;
        private set => SetField(ref _horizontalOffsetError, value);
    }

    public string? VerticalOffsetError
    {
        get => _verticalOffsetError;
        private set => SetField(ref _verticalOffsetError, value);
    }

    public string? HoverPreviewDelayError
    {
        get => _hoverPreviewDelayError;
        private set => SetField(ref _hoverPreviewDelayError, value);
    }

    public string? FullscreenExceptionsError
    {
        get => _fullscreenExceptionsError;
        private set => SetField(ref _fullscreenExceptionsError, value);
    }

    public bool HasErrors =>
        HorizontalOffsetError is not null || VerticalOffsetError is not null || HoverPreviewDelayError is not null || FullscreenExceptionsError is not null;

    /// <summary>True when the draft differs from <see cref="Saved"/> (an invalid entry always counts as a change).</summary>
    public bool IsDirty => HasErrors || !SameSettings(BuildDraft(), Saved);

    public bool CanSave => IsDirty && !HasErrors;

    /// <summary>Discards the draft and starts again from <paramref name="saved"/>.</summary>
    public void Load(AppSettings saved)
    {
        ArgumentNullException.ThrowIfNull(saved);

        _loading = true;
        try
        {
            Saved = saved;
            DisplayOptions = BuildDisplayOptions(_monitors, saved.General.PreferredMonitorId);
            LoadGeneral(saved.General);
            _startWithWindows = saved.General.StartWithWindows == ConsentState.Granted;
            _dailyUpdateChecks = saved.Updates.AutomaticChecks == ConsentState.Granted;
            LoadAppearance(saved.Appearance);
            LoadBehavior(saved.Behavior);
            LoadWidgets(saved.Widgets);
        }
        finally
        {
            _loading = false;
        }

        RaiseAll();
    }

    /// <summary>Discards the draft (same as loading <see cref="Saved"/> again).</summary>
    public void Revert() => Load(Saved);

    /// <summary>Puts the defaults of one page into the draft (not saved until Save). Consent settings are never reset.</summary>
    public void ResetSection(SettingsSection section)
    {
        var defaults = new AppSettings();
        _loading = true;
        try
        {
            switch (section)
            {
                case SettingsSection.General:
                    LoadGeneral(defaults.General);
                    break;
                case SettingsSection.Appearance:
                    LoadAppearance(defaults.Appearance);
                    break;
                case SettingsSection.Behavior:
                    LoadBehavior(defaults.Behavior);
                    break;
                case SettingsSection.Widgets:
                    LoadWidgets(defaults.Widgets);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(section), section, null);
            }
        }
        finally
        {
            _loading = false;
        }

        RaiseAll();
    }

    public void ResetAll()
    {
        foreach (var section in Enum.GetValues<SettingsSection>())
        {
            ResetSection(section);
        }
    }

    /// <summary>Moves a widget up (negative) or down (positive) in the display order; out-of-range moves are ignored.</summary>
    /// <returns>The widget's new index.</returns>
    public int MoveWidget(int index, int delta)
    {
        var target = index + delta;
        if (index < 0 || index >= Widgets.Count || target < 0 || target >= Widgets.Count)
        {
            return index;
        }

        Widgets.Move(index, target);
        return target;
    }

    /// <summary>
    /// The settings to save: <paramref name="latest"/> with only the fields the user changed (draft ≠ <see cref="Saved"/>)
    /// replaced by the draft.
    /// </summary>
    /// <exception cref="InvalidOperationException">The draft has validation errors.</exception>
    public AppSettings ApplyTo(AppSettings latest) => CreateChange()(latest);

    /// <summary>
    /// <see cref="ApplyTo"/> over a snapshot of the draft taken now, for <see cref="ISettingsService.UpdateAsync"/>
    /// (which may run the change on another thread while the user keeps editing).
    /// </summary>
    /// <exception cref="InvalidOperationException">The draft has validation errors.</exception>
    public Func<AppSettings, AppSettings> CreateChange()
    {
        if (HasErrors)
        {
            throw new InvalidOperationException("The draft has validation errors.");
        }

        var draft = BuildDraft();
        var saved = Saved;
        return latest => Merge(latest, saved, draft);
    }

    private static AppSettings Merge(AppSettings latest, AppSettings saved, AppSettings draft)
    {
        ArgumentNullException.ThrowIfNull(latest);
        return latest with
        {
            General = latest.General with
            {
                PreferredMonitorId = Pick(latest.General.PreferredMonitorId, saved.General.PreferredMonitorId, draft.General.PreferredMonitorId),
                StartWithWindows = Pick(latest.General.StartWithWindows, saved.General.StartWithWindows, draft.General.StartWithWindows),
            },
            Updates = latest.Updates with
            {
                AutomaticChecks = Pick(latest.Updates.AutomaticChecks, saved.Updates.AutomaticChecks, draft.Updates.AutomaticChecks),
            },
            Appearance = new AppearanceSettings
            {
                Theme = Pick(latest.Appearance.Theme, saved.Appearance.Theme, draft.Appearance.Theme),
                Scale = Pick(latest.Appearance.Scale, saved.Appearance.Scale, draft.Appearance.Scale),
                Opacity = Pick(latest.Appearance.Opacity, saved.Appearance.Opacity, draft.Appearance.Opacity),
                Placement = Pick(latest.Appearance.Placement, saved.Appearance.Placement, draft.Appearance.Placement),
                HorizontalOffset = Pick(latest.Appearance.HorizontalOffset, saved.Appearance.HorizontalOffset, draft.Appearance.HorizontalOffset),
                VerticalOffset = Pick(latest.Appearance.VerticalOffset, saved.Appearance.VerticalOffset, draft.Appearance.VerticalOffset),
                Motion = Pick(latest.Appearance.Motion, saved.Appearance.Motion, draft.Appearance.Motion),
                Clock = new ClockSettings
                {
                    HourFormat = Pick(latest.Appearance.Clock.HourFormat, saved.Appearance.Clock.HourFormat, draft.Appearance.Clock.HourFormat),
                    ShowSeconds = Pick(latest.Appearance.Clock.ShowSeconds, saved.Appearance.Clock.ShowSeconds, draft.Appearance.Clock.ShowSeconds),
                    DateFormat = Pick(latest.Appearance.Clock.DateFormat, saved.Appearance.Clock.DateFormat, draft.Appearance.Clock.DateFormat),
                },
            },
            Behavior = new BehaviorSettings
            {
                HoverPreviewEnabled = Pick(latest.Behavior.HoverPreviewEnabled, saved.Behavior.HoverPreviewEnabled, draft.Behavior.HoverPreviewEnabled),
                HoverPreviewDelayMs = Pick(latest.Behavior.HoverPreviewDelayMs, saved.Behavior.HoverPreviewDelayMs, draft.Behavior.HoverPreviewDelayMs),
                AutoHideInFullscreen = Pick(latest.Behavior.AutoHideInFullscreen, saved.Behavior.AutoHideInFullscreen, draft.Behavior.AutoHideInFullscreen),
                FullscreenExceptions = SameList(draft.Behavior.FullscreenExceptions, saved.Behavior.FullscreenExceptions, StringComparer.OrdinalIgnoreCase)
                    ? latest.Behavior.FullscreenExceptions
                    : draft.Behavior.FullscreenExceptions,
            },
            Widgets = SameList(draft.Widgets, saved.Widgets, EqualityComparer<WidgetSetting>.Default) ? latest.Widgets : draft.Widgets,
        };
    }

    /// <summary>User-facing name of a built-in widget (settings and onboarding).</summary>
    public static string WidgetLabel(string id) => WidgetLabels.GetValueOrDefault(id, id);

    /// <summary>"Primary display" first, then "Display N (primary)" in tray order; a saved but disconnected display is kept.</summary>
    public static IReadOnlyList<DisplayOption> BuildDisplayOptions(IReadOnlyList<MonitorInfo> monitors, string? preferredMonitorId)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        var options = new List<DisplayOption> { new(null, "Primary display (follows Windows)") };
        var ordered = TrayMenuModel.OrderDisplays(monitors);
        for (var i = 0; i < ordered.Length; i++)
        {
            options.Add(new DisplayOption(ordered[i].Id, $"Display {i + 1}" + (ordered[i].IsPrimary ? " (primary)" : string.Empty)));
        }

        if (preferredMonitorId is not null && !options.Any(o => string.Equals(o.Id, preferredMonitorId, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new DisplayOption(preferredMonitorId, "Saved display (not connected)"));
        }

        return options;
    }

    private void LoadGeneral(GeneralSettings general) =>
        _selectedDisplay = DisplayOptions.FirstOrDefault(o => string.Equals(o.Id, general.PreferredMonitorId, StringComparison.OrdinalIgnoreCase)) ?? DisplayOptions[0];

    private void LoadAppearance(AppearanceSettings appearance)
    {
        _theme = appearance.Theme;
        _scale = appearance.Scale;
        _opacity = appearance.Opacity;
        _placement = appearance.Placement;
        _horizontalOffsetText = Format(appearance.HorizontalOffset);
        _verticalOffsetText = Format(appearance.VerticalOffset);
        _motion = appearance.Motion;
        _clockHourFormat = appearance.Clock.HourFormat;
        _clockShowSeconds = appearance.Clock.ShowSeconds;
        _clockDateFormat = appearance.Clock.DateFormat;
    }

    private void LoadBehavior(BehaviorSettings behavior)
    {
        _hoverPreviewEnabled = behavior.HoverPreviewEnabled;
        _hoverPreviewDelayText = behavior.HoverPreviewDelayMs.ToString(_culture);
        _autoHideInFullscreen = behavior.AutoHideInFullscreen;
        _fullscreenExceptionsText = string.Join(Environment.NewLine, behavior.FullscreenExceptions);
    }

    private void LoadWidgets(IReadOnlyList<WidgetSetting> widgets)
    {
        Widgets.Clear();
        foreach (var widget in widgets)
        {
            var option = new WidgetOption(widget.Id, WidgetLabel(widget.Id), widget.Enabled);
            option.EnabledChanged += (_, _) => OnDraftChanged();
            Widgets.Add(option);
        }
    }

    /// <summary>The draft as settings; fields with errors keep their saved value (only used when there are none, or for the dirty check).</summary>
    private AppSettings BuildDraft() => Saved with
    {
        General = Saved.General with
        {
            PreferredMonitorId = _selectedDisplay.Id,
            StartWithWindows = Consent(Saved.General.StartWithWindows, _startWithWindows),
        },
        Updates = Saved.Updates with { AutomaticChecks = Consent(Saved.Updates.AutomaticChecks, _dailyUpdateChecks) },
        Appearance = new AppearanceSettings
        {
            Theme = _theme,
            Scale = _scale,
            Opacity = _opacity,
            Placement = _placement,
            HorizontalOffset = ParseDouble(_horizontalOffsetText, -SettingsValidator.MaxHorizontalOffset, SettingsValidator.MaxHorizontalOffset) ?? Saved.Appearance.HorizontalOffset,
            VerticalOffset = ParseDouble(_verticalOffsetText, 0, SettingsValidator.MaxVerticalOffset) ?? Saved.Appearance.VerticalOffset,
            Motion = _motion,
            Clock = new ClockSettings { HourFormat = _clockHourFormat, ShowSeconds = _clockShowSeconds, DateFormat = _clockDateFormat },
        },
        Behavior = new BehaviorSettings
        {
            HoverPreviewEnabled = _hoverPreviewEnabled,
            HoverPreviewDelayMs = ParseDelay(_hoverPreviewDelayText) ?? Saved.Behavior.HoverPreviewDelayMs,
            AutoHideInFullscreen = _autoHideInFullscreen,
            FullscreenExceptions = ParseExceptions(_fullscreenExceptionsText, out _) ?? Saved.Behavior.FullscreenExceptions,
        },
        Widgets = [.. Widgets.Select(w => new WidgetSetting(w.Id, w.Enabled))],
    };

    private void Validate()
    {
        HorizontalOffsetError = ParseDouble(_horizontalOffsetText, -SettingsValidator.MaxHorizontalOffset, SettingsValidator.MaxHorizontalOffset) is null
            ? $"Enter a number from {Format(-SettingsValidator.MaxHorizontalOffset)} to {Format(SettingsValidator.MaxHorizontalOffset)}."
            : null;
        VerticalOffsetError = ParseDouble(_verticalOffsetText, 0, SettingsValidator.MaxVerticalOffset) is null
            ? $"Enter a number from 0 to {Format(SettingsValidator.MaxVerticalOffset)}."
            : null;
        HoverPreviewDelayError = ParseDelay(_hoverPreviewDelayText) is null
            ? $"Enter a whole number of milliseconds from {SettingsValidator.MinHoverPreviewDelayMs} to {SettingsValidator.MaxHoverPreviewDelayMs}."
            : null;
        ParseExceptions(_fullscreenExceptionsText, out var exceptionsError);
        FullscreenExceptionsError = exceptionsError;
    }

    private double? ParseDouble(string text, double min, double max) =>
        double.TryParse(text.Trim(), NumberStyles.Float, _culture, out var value) && double.IsFinite(value) && value >= min && value <= max
            ? value
            : null;

    private int? ParseDelay(string text) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, _culture, out var value)
        && value is >= SettingsValidator.MinHoverPreviewDelayMs and <= SettingsValidator.MaxHoverPreviewDelayMs
            ? value
            : null;

    /// <summary>Program file names, one per line, lower-cased and de-duplicated; <c>null</c> with a message when a line is invalid.</summary>
    private static string[]? ParseExceptions(string text, out string? error)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var names = new List<string>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var name = lines[i].Trim().ToLowerInvariant();
            if (name.Length == 0)
            {
                continue;
            }

            if (name.Length > SettingsValidator.MaxExecutableNameLength || name.IndexOfAny(invalid) >= 0 || !name.EndsWith(".exe", StringComparison.Ordinal) || name.Length == 4)
            {
                error = $"Line {i + 1} is not a program file name such as vlc.exe (no folder).";
                return null;
            }

            if (!names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }
        }

        if (names.Count > SettingsValidator.MaxFullscreenExceptions)
        {
            error = $"Enter at most {SettingsValidator.MaxFullscreenExceptions} programs.";
            return null;
        }

        error = null;
        return [.. names];
    }

    private string Format(double value) => value.ToString("0.##", _culture);

    /// <summary>The saved consent while the box is unchanged (keeps <see cref="ConsentState.NotAsked"/>), otherwise the explicit choice.</summary>
    private static ConsentState Consent(ConsentState saved, bool enabled) =>
        enabled == (saved == ConsentState.Granted) ? saved : enabled ? ConsentState.Granted : ConsentState.Declined;

    private static T Pick<T>(T latest, T saved, T draft) => EqualityComparer<T>.Default.Equals(draft, saved) ? latest : draft;

    private static bool SameList<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, IEqualityComparer<T> comparer) => a.SequenceEqual(b, comparer);

    private static bool SameSettings(AppSettings a, AppSettings b) =>
        a.General == b.General
        && a.Appearance == b.Appearance
        && a.Behavior == b.Behavior
        && a.Updates == b.Updates
        && SameList(a.Widgets, b.Widgets, EqualityComparer<WidgetSetting>.Default);

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        OnDraftChanged();
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void OnDraftChanged()
    {
        if (Saved is null || _loading)
        {
            return; // Constructing or loading: RaiseAll follows.
        }

        Validate();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasErrors)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSave)));
    }

    private void RaiseAll()
    {
        Validate();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
