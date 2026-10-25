using DynamicIsland.Core.Settings;

namespace DynamicIsland.UnitTests.Settings;

public sealed class SettingsValidatorTests
{
    [Fact]
    public void Defaults_are_valid_and_need_no_corrections()
    {
        var result = SettingsValidator.Normalize(new AppSettings());

        Assert.False(result.WasCorrected);
        Assert.Equal(WidgetIds.All, result.Settings.Widgets.Select(w => w.Id));
        Assert.All(result.Settings.Widgets, w => Assert.True(w.Enabled));
        Assert.Equal(ConsentState.NotAsked, result.Settings.General.StartWithWindows);
        Assert.Equal(ConsentState.NotAsked, result.Settings.Updates.AutomaticChecks);
    }

    [Fact]
    public void Null_settings_become_defaults()
    {
        var result = SettingsValidator.Normalize(null);

        Assert.True(result.WasCorrected);
        Assert.Equal(new AppSettings().Appearance, result.Settings.Appearance);
    }

    [Theory]
    [InlineData(0.1, SettingsValidator.MinScale)]
    [InlineData(9.0, SettingsValidator.MaxScale)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    public void Scale_is_clamped_or_reset(double input, double expected)
    {
        var settings = new AppSettings { Appearance = new AppearanceSettings { Scale = input } };

        var result = SettingsValidator.Normalize(settings);

        Assert.Equal(expected, result.Settings.Appearance.Scale);
        Assert.Contains(result.Corrections, c => c.StartsWith("appearance.scale", StringComparison.Ordinal));
    }

    [Fact]
    public void Out_of_range_values_are_clamped()
    {
        var settings = new AppSettings
        {
            Appearance = new AppearanceSettings { Opacity = 0.01 },
            Behavior = new BehaviorSettings { HoverPreviewDelayMs = 60_000 },
        };

        var result = SettingsValidator.Normalize(settings);

        Assert.Equal(SettingsValidator.MinOpacity, result.Settings.Appearance.Opacity);
        Assert.Equal(SettingsValidator.MaxHoverPreviewDelayMs, result.Settings.Behavior.HoverPreviewDelayMs);
        Assert.Equal(2, result.Corrections.Count);
    }

    [Fact]
    public void Placement_offsets_are_clamped()
    {
        var settings = new AppSettings { Appearance = new AppearanceSettings { HorizontalOffset = -99_999, VerticalOffset = -4 } };

        var result = SettingsValidator.Normalize(settings);

        Assert.Equal(-SettingsValidator.MaxHorizontalOffset, result.Settings.Appearance.HorizontalOffset);
        Assert.Equal(0, result.Settings.Appearance.VerticalOffset);
        Assert.Equal(2, result.Corrections.Count);
    }

    [Fact]
    public void Undefined_enum_values_fall_back_to_defaults()
    {
        var settings = new AppSettings
        {
            Appearance = new AppearanceSettings { Theme = (ThemeMode)42, Motion = (MotionPreference)(-1) },
            General = new GeneralSettings { StartWithWindows = (ConsentState)7 },
        };

        var result = SettingsValidator.Normalize(settings);

        Assert.Equal(ThemeMode.System, result.Settings.Appearance.Theme);
        Assert.Equal(MotionPreference.System, result.Settings.Appearance.Motion);
        Assert.Equal(ConsentState.NotAsked, result.Settings.General.StartWithWindows);
        Assert.Equal(3, result.Corrections.Count);
    }

    [Fact]
    public void Null_sections_are_replaced_with_defaults()
    {
        var settings = new AppSettings { General = null!, Appearance = null!, Behavior = null!, Updates = null!, Widgets = null! };

        var result = SettingsValidator.Normalize(settings);

        Assert.Equal(new AppSettings().Behavior, result.Settings.Behavior);
        Assert.Equal(WidgetIds.All, result.Settings.Widgets.Select(w => w.Id));
        Assert.Equal(5, result.Corrections.Count);
    }

    [Fact]
    public void Widgets_keep_user_order_drop_unknown_and_duplicates_and_append_missing()
    {
        var settings = new AppSettings
        {
            Widgets =
            [
                new WidgetSetting(WidgetIds.Notes, false),
                new WidgetSetting("weather", true),
                new WidgetSetting(WidgetIds.Clock, true),
                new WidgetSetting(WidgetIds.Notes, true),
                null!,
            ],
        };

        var result = SettingsValidator.Normalize(settings);

        string[] expectedOrder = [WidgetIds.Notes, WidgetIds.Clock, WidgetIds.Media, WidgetIds.SystemStats, WidgetIds.Volume, WidgetIds.Tasks];
        Assert.Equal(expectedOrder, result.Settings.Widgets.Select(w => w.Id));
        Assert.False(result.Settings.Widgets[0].Enabled);
        Assert.Contains("widgets: duplicate entry removed", result.Corrections);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_monitor_id_means_primary_monitor(string monitorId)
    {
        var settings = new AppSettings { General = new GeneralSettings { PreferredMonitorId = monitorId } };

        var result = SettingsValidator.Normalize(settings);

        Assert.Null(result.Settings.General.PreferredMonitorId);
    }

    [Fact]
    public void Overlong_monitor_id_is_rejected()
    {
        var settings = new AppSettings { General = new GeneralSettings { PreferredMonitorId = new string('x', SettingsValidator.MaxMonitorIdLength + 1) } };

        Assert.Null(SettingsValidator.Normalize(settings).Settings.General.PreferredMonitorId);
    }

    [Fact]
    public void Corrections_never_contain_values()
    {
        var settings = new AppSettings { General = new GeneralSettings { PreferredMonitorId = new string('s', 600) } };

        var result = SettingsValidator.Normalize(settings);

        Assert.All(result.Corrections, c => Assert.DoesNotContain("sss", c, StringComparison.Ordinal));
    }
}
