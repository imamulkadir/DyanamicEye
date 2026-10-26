using System.Globalization;
using DynamicIsland.Core.Display;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;

namespace DynamicIsland.UnitTests.Shell;

public sealed class SettingsEditorTests
{
    private static readonly MonitorInfo Primary = new("primary-id", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 1.0, IsPrimary: true);
    private static readonly MonitorInfo Left = new("left-id", new(-2560, 0, 2560, 1440), new(-2560, 0, 2560, 1400), 1.5, IsPrimary: false);

    private static SettingsEditor Create(AppSettings? saved = null, CultureInfo? culture = null) =>
        new(saved ?? new AppSettings(), [Primary, Left], culture ?? CultureInfo.InvariantCulture);

    [Fact]
    public void Fresh_editor_shows_the_saved_values_and_is_clean()
    {
        var saved = new AppSettings
        {
            General = new GeneralSettings { PreferredMonitorId = "left-id" },
            Appearance = new AppearanceSettings { HorizontalOffset = -12.5, Theme = ThemeMode.Dark },
            Behavior = new BehaviorSettings { HoverPreviewDelayMs = 450, FullscreenExceptions = ["vlc.exe", "mpv.exe"] },
        };

        var editor = Create(saved);

        Assert.Equal("left-id", editor.SelectedDisplay.Id);
        Assert.Equal("-12.5", editor.HorizontalOffsetText);
        Assert.Equal(ThemeMode.Dark, editor.Theme);
        Assert.Equal("450", editor.HoverPreviewDelayText);
        Assert.Equal($"vlc.exe{Environment.NewLine}mpv.exe", editor.FullscreenExceptionsText);
        Assert.False(editor.IsDirty);
        Assert.False(editor.CanSave);
        Assert.False(editor.HasErrors);
    }

    [Fact]
    public void Display_options_start_with_primary_then_tray_numbering_and_keep_a_disconnected_choice()
    {
        var options = SettingsEditor.BuildDisplayOptions([Primary, Left], "gone-id");

        Assert.Equal([null, "left-id", "primary-id", "gone-id"], options.Select(o => o.Id));
        Assert.Equal(["Primary display (follows Windows)", "Display 1", "Display 2 (primary)", "Saved display (not connected)"], options.Select(o => o.Label));
    }

    [Fact]
    public void Changing_a_value_makes_the_draft_dirty_and_changing_it_back_makes_it_clean()
    {
        var editor = Create();
        var changes = new List<string?>();
        editor.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        editor.Opacity = 0.5;
        Assert.True(editor.IsDirty);
        Assert.True(editor.CanSave);
        Assert.Contains(nameof(SettingsEditor.CanSave), changes);

        editor.Opacity = 1.0;
        Assert.False(editor.IsDirty);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("4000.5")]
    [InlineData("")]
    [InlineData("NaN")]
    public void Invalid_horizontal_offset_shows_an_error_and_blocks_save(string text)
    {
        var editor = Create();

        editor.HorizontalOffsetText = text;

        Assert.NotNull(editor.HorizontalOffsetError);
        Assert.True(editor.HasErrors);
        Assert.True(editor.IsDirty);
        Assert.False(editor.CanSave);
        Assert.Throws<InvalidOperationException>(() => editor.ApplyTo(new AppSettings()));

        editor.HorizontalOffsetText = "-4000";
        Assert.Null(editor.HorizontalOffsetError);
        Assert.True(editor.CanSave);
    }

    [Theory]
    [InlineData("-1", false)]
    [InlineData("1000", true)]
    [InlineData("1000.01", false)]
    public void Vertical_offset_must_be_between_zero_and_the_maximum(string text, bool valid)
    {
        var editor = Create();

        editor.VerticalOffsetText = text;

        Assert.Equal(valid, editor.VerticalOffsetError is null);
    }

    [Theory]
    [InlineData("99", false)]
    [InlineData("100", true)]
    [InlineData("2000", true)]
    [InlineData("2001", false)]
    [InlineData("250.5", false)]
    public void Hover_delay_must_be_a_whole_number_in_range(string text, bool valid)
    {
        var editor = Create();

        editor.HoverPreviewDelayText = text;

        Assert.Equal(valid, editor.HoverPreviewDelayError is null);
    }

    [Fact]
    public void Numbers_follow_the_users_culture()
    {
        var german = CultureInfo.GetCultureInfo("de-DE");
        var editor = Create(new AppSettings { Appearance = new AppearanceSettings { HorizontalOffset = 2.5 } }, german);

        Assert.Equal("2,5", editor.HorizontalOffsetText);
        editor.HorizontalOffsetText = "7,25";

        Assert.Null(editor.HorizontalOffsetError);
        Assert.Equal(7.25, editor.ApplyTo(editor.Saved).Appearance.HorizontalOffset);
    }

    [Theory]
    [InlineData("vlc.exe\r\n\r\n  MPV.EXE  \nvlc.exe", null)]
    [InlineData("C:\\Program Files\\vlc.exe", 1)]
    [InlineData("vlc.exe\nnotepad", 2)]
    [InlineData("vlc.exe\n.exe", 2)]
    public void Fullscreen_exceptions_are_program_names_one_per_line(string text, int? badLine)
    {
        var editor = Create();

        editor.FullscreenExceptionsText = text;

        if (badLine is null)
        {
            Assert.Null(editor.FullscreenExceptionsError);
            Assert.Equal(["vlc.exe", "mpv.exe"], editor.ApplyTo(editor.Saved).Behavior.FullscreenExceptions);
        }
        else
        {
            Assert.Contains($"Line {badLine}", editor.FullscreenExceptionsError, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Too_many_fullscreen_exceptions_is_an_error()
    {
        var editor = Create();

        editor.FullscreenExceptionsText = string.Join('\n', Enumerable.Range(0, SettingsValidator.MaxFullscreenExceptions + 1).Select(i => $"app{i}.exe"));

        Assert.NotNull(editor.FullscreenExceptionsError);
    }

    [Fact]
    public void Sliders_are_rounded_and_kept_in_range()
    {
        var editor = Create();

        editor.Scale = 1.2349;
        editor.Opacity = 0.01;

        Assert.Equal(1.25, editor.Scale);
        Assert.Equal(SettingsValidator.MinOpacity, editor.Opacity);
    }

    [Fact]
    public void Save_applies_only_changed_fields_and_keeps_concurrent_changes()
    {
        var editor = Create();
        editor.Theme = ThemeMode.Light;

        // Meanwhile the tray moved the island to another display and nudged it.
        var latest = new AppSettings
        {
            General = new GeneralSettings { PreferredMonitorId = "left-id", StartWithWindows = ConsentState.Granted },
            Appearance = new AppearanceSettings { VerticalOffset = 8 },
        };

        var result = editor.ApplyTo(latest);

        Assert.Equal(ThemeMode.Light, result.Appearance.Theme);
        Assert.Equal("left-id", result.General.PreferredMonitorId);
        Assert.Equal(8, result.Appearance.VerticalOffset);
        Assert.Equal(ConsentState.Granted, result.General.StartWithWindows);
    }

    [Fact]
    public void Save_writes_a_changed_display_and_widget_list()
    {
        var editor = Create();
        editor.SelectedDisplay = editor.DisplayOptions.Single(o => o.Id == "left-id");
        editor.Widgets[1].Enabled = false;
        Assert.Equal(0, editor.MoveWidget(1, -1));

        var result = editor.ApplyTo(new AppSettings());

        Assert.Equal("left-id", result.General.PreferredMonitorId);
        Assert.Equal(new WidgetSetting(WidgetIds.Media, false), result.Widgets[0]);
        Assert.Equal(WidgetIds.Clock, result.Widgets[1].Id);
    }

    [Fact]
    public void Widget_moves_out_of_range_are_ignored()
    {
        var editor = Create();

        Assert.Equal(0, editor.MoveWidget(0, -1));
        Assert.Equal(editor.Widgets.Count - 1, editor.MoveWidget(editor.Widgets.Count - 1, 1));
        Assert.Equal(7, editor.MoveWidget(7, 1));
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void Toggling_a_widget_makes_the_draft_dirty()
    {
        var editor = Create();

        editor.Widgets[0].Enabled = false;

        Assert.True(editor.IsDirty);
        Assert.Equal("Clock and date", editor.Widgets[0].Label);
    }

    [Fact]
    public void Reset_puts_defaults_into_the_draft_but_never_touches_consent()
    {
        var saved = new AppSettings
        {
            General = new GeneralSettings { PreferredMonitorId = "left-id", StartWithWindows = ConsentState.Granted },
            Appearance = new AppearanceSettings { Opacity = 0.5, Placement = IslandPlacement.TopLeft },
            Behavior = new BehaviorSettings { HoverPreviewEnabled = false },
            Updates = new UpdateSettings { AutomaticChecks = ConsentState.Declined },
            Widgets = [.. WidgetIds.All.Reverse().Select(id => new WidgetSetting(id, false))],
        };
        var editor = Create(saved);

        editor.ResetSection(SettingsSection.Appearance);
        Assert.Equal(1.0, editor.Opacity);
        Assert.Equal(IslandPlacement.TopCenter, editor.Placement);
        Assert.False(editor.HoverPreviewEnabled);

        editor.ResetAll();
        var result = editor.ApplyTo(saved);

        Assert.Null(result.General.PreferredMonitorId);
        Assert.True(result.Behavior.HoverPreviewEnabled);
        Assert.Equal(WidgetIds.All, result.Widgets.Select(w => w.Id));
        Assert.All(result.Widgets, w => Assert.True(w.Enabled));
        Assert.Equal(ConsentState.Granted, result.General.StartWithWindows);
        Assert.Equal(ConsentState.Declined, result.Updates.AutomaticChecks);
        Assert.Equal(saved, editor.Saved);
    }

    [Fact]
    public void Revert_discards_the_draft_and_load_starts_from_new_settings()
    {
        var editor = Create();
        editor.HoverPreviewDelayText = "nope";

        editor.Revert();
        Assert.False(editor.IsDirty);
        Assert.Null(editor.HoverPreviewDelayError);

        var newer = new AppSettings { Behavior = new BehaviorSettings { HoverPreviewDelayMs = 900 } };
        editor.Load(newer);
        Assert.Equal("900", editor.HoverPreviewDelayText);
        Assert.Same(newer, editor.Saved);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void Untouched_consent_boxes_keep_not_asked_and_toggles_are_explicit()
    {
        var editor = Create();
        Assert.False(editor.StartWithWindows);

        editor.HoverPreviewEnabled = false;
        var untouched = editor.ApplyTo(editor.Saved);
        Assert.Equal(ConsentState.NotAsked, untouched.General.StartWithWindows);
        Assert.Equal(ConsentState.NotAsked, untouched.Updates.AutomaticChecks);

        editor.StartWithWindows = true;
        editor.DailyUpdateChecks = true;
        editor.DailyUpdateChecks = false;
        var chosen = editor.ApplyTo(editor.Saved);
        Assert.Equal(ConsentState.Granted, chosen.General.StartWithWindows);
        Assert.Equal(ConsentState.NotAsked, chosen.Updates.AutomaticChecks);
    }

    [Fact]
    public void Clearing_a_granted_consent_declines_it()
    {
        var editor = Create(new AppSettings { General = new GeneralSettings { StartWithWindows = ConsentState.Granted } });
        Assert.True(editor.StartWithWindows);

        editor.StartWithWindows = false;

        Assert.True(editor.IsDirty);
        Assert.Equal(ConsentState.Declined, editor.ApplyTo(editor.Saved).General.StartWithWindows);
    }

    [Fact]
    public void Reset_all_keeps_a_ticked_but_unsaved_consent_box()
    {
        var editor = Create();
        editor.StartWithWindows = true;

        editor.ResetAll();

        Assert.True(editor.StartWithWindows);
    }

    [Fact]
    public void Clock_options_are_edited_previewed_merged_and_reset_with_appearance()
    {
        var editor = Create(culture: CultureInfo.GetCultureInfo("en-US"));
        var afternoon = new DateTime(2026, 10, 26, 14, 42, 5, DateTimeKind.Local);
        Assert.Equal("2:42 PM", editor.ClockPreview(afternoon));

        editor.ClockShowSeconds = true;
        editor.ClockDateFormat = ClockDateFormat.Weekday;
        Assert.Equal("Mon · 2:42:05 PM", editor.ClockPreview(afternoon));

        var latest = new AppSettings { Appearance = new AppearanceSettings { Clock = new ClockSettings { HourFormat = ClockHourFormat.TwentyFourHour } } };
        var result = editor.ApplyTo(latest).Appearance.Clock;
        Assert.Equal(new ClockSettings { HourFormat = ClockHourFormat.TwentyFourHour, ShowSeconds = true, DateFormat = ClockDateFormat.Weekday }, result);

        editor.ResetSection(SettingsSection.Appearance);
        Assert.False(editor.ClockShowSeconds);
        Assert.Equal(ClockDateFormat.None, editor.ClockDateFormat);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void Shortcut_is_validated_and_merged_and_reset_with_behavior()
    {
        var editor = Create();
        Assert.Equal("Ctrl+Alt+I", editor.ShortcutText);

        editor.ShortcutText = "ctrl + alt + i";
        Assert.False(editor.IsDirty); // Same shortcut, different spelling.

        editor.ShortcutText = "Win+K";
        Assert.NotNull(editor.ShortcutError);
        Assert.False(editor.CanSave);

        editor.ShortcutEnabled = true;
        editor.ShortcutText = "shift+win+k";
        Assert.Null(editor.ShortcutError);
        Assert.Equal(new ShortcutSettings { Enabled = true, Keys = "Shift+Win+K" }, editor.ApplyTo(editor.Saved).Behavior.Shortcut);

        editor.ResetSection(SettingsSection.Behavior);
        Assert.False(editor.ShortcutEnabled);
        Assert.Equal("Ctrl+Alt+I", editor.ShortcutText);
    }

    [Fact]
    public void Loading_never_reports_a_half_loaded_draft_as_dirty()
    {
        var editor = Create();
        var dirtySeen = false;
        editor.PropertyChanged += (_, _) => dirtySeen |= editor.IsDirty;

        editor.Load(new AppSettings { Appearance = new AppearanceSettings { Opacity = 0.4 } });
        editor.ResetSection(SettingsSection.Widgets);

        Assert.False(dirtySeen);
    }

    [Fact]
    public void Saved_result_passes_the_loaders_validation_unchanged()
    {
        var editor = Create();
        editor.HorizontalOffsetText = "4000";
        editor.VerticalOffsetText = "1000";
        editor.HoverPreviewDelayText = "100";
        editor.Scale = 2;
        editor.FullscreenExceptionsText = "game.exe";

        var result = editor.ApplyTo(editor.Saved);

        Assert.False(SettingsValidator.Normalize(result).WasCorrected);
    }
}
