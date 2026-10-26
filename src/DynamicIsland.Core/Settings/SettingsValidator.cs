namespace DynamicIsland.Core.Settings;

public sealed record SettingsValidationResult(AppSettings Settings, IReadOnlyList<string> Corrections)
{
    public bool WasCorrected => Corrections.Count > 0;
}

/// <summary>
/// Repairs settings instead of rejecting them: missing sections and invalid values fall back to defaults,
/// numbers are clamped, widget list is de-duplicated and completed. Corrections name the setting, never its value.
/// </summary>
public static class SettingsValidator
{
    public const double MinScale = 0.75;
    public const double MaxScale = 2.0;
    public const double MinOpacity = 0.3;
    public const double MaxOpacity = 1.0;
    public const int MinHoverPreviewDelayMs = 100;
    public const int MaxHoverPreviewDelayMs = 2000;
    public const int MaxMonitorIdLength = 512;
    public const int MaxFullscreenExceptions = 50;
    public const int MaxExecutableNameLength = 260;
    public const double MaxHorizontalOffset = 4000;
    public const double MaxVerticalOffset = 1000;

    public static SettingsValidationResult Normalize(AppSettings? settings)
    {
        var corrections = new List<string>();
        if (settings is null)
        {
            corrections.Add("settings: missing, defaults used");
            return new SettingsValidationResult(new AppSettings(), corrections);
        }

        var normalized = new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            General = NormalizeGeneral(settings.General, corrections),
            Appearance = NormalizeAppearance(settings.Appearance, corrections),
            Behavior = NormalizeBehavior(settings.Behavior, corrections),
            Widgets = NormalizeWidgets(settings.Widgets, corrections),
            Updates = NormalizeUpdates(settings.Updates, corrections),
        };

        return new SettingsValidationResult(normalized, corrections);
    }

    private static GeneralSettings NormalizeGeneral(GeneralSettings? general, List<string> corrections)
    {
        var defaults = new GeneralSettings();
        if (general is null)
        {
            corrections.Add("general: missing, defaults used");
            return defaults;
        }

        var monitorId = general.PreferredMonitorId;
        if (monitorId is not null && (string.IsNullOrWhiteSpace(monitorId) || monitorId.Length > MaxMonitorIdLength))
        {
            corrections.Add("general.preferredMonitorId: invalid, primary monitor used");
            monitorId = null;
        }

        return new GeneralSettings
        {
            StartWithWindows = Defined(general.StartWithWindows, defaults.StartWithWindows, "general.startWithWindows", corrections),
            PreferredMonitorId = monitorId,
        };
    }

    private static AppearanceSettings NormalizeAppearance(AppearanceSettings? appearance, List<string> corrections)
    {
        var defaults = new AppearanceSettings();
        if (appearance is null)
        {
            corrections.Add("appearance: missing, defaults used");
            return defaults;
        }

        return new AppearanceSettings
        {
            Theme = Defined(appearance.Theme, defaults.Theme, "appearance.theme", corrections),
            Scale = Clamp(appearance.Scale, MinScale, MaxScale, defaults.Scale, "appearance.scale", corrections),
            Opacity = Clamp(appearance.Opacity, MinOpacity, MaxOpacity, defaults.Opacity, "appearance.opacity", corrections),
            Placement = Defined(appearance.Placement, defaults.Placement, "appearance.placement", corrections),
            HorizontalOffset = Clamp(appearance.HorizontalOffset, -MaxHorizontalOffset, MaxHorizontalOffset, defaults.HorizontalOffset, "appearance.horizontalOffset", corrections),
            VerticalOffset = Clamp(appearance.VerticalOffset, 0, MaxVerticalOffset, defaults.VerticalOffset, "appearance.verticalOffset", corrections),
            Motion = Defined(appearance.Motion, defaults.Motion, "appearance.motion", corrections),
            Clock = NormalizeClock(appearance.Clock, corrections),
        };
    }

    private static ClockSettings NormalizeClock(ClockSettings? clock, List<string> corrections)
    {
        var defaults = new ClockSettings();
        if (clock is null)
        {
            corrections.Add("appearance.clock: missing, defaults used");
            return defaults;
        }

        return clock with
        {
            HourFormat = Defined(clock.HourFormat, defaults.HourFormat, "appearance.clock.hourFormat", corrections),
            DateFormat = Defined(clock.DateFormat, defaults.DateFormat, "appearance.clock.dateFormat", corrections),
        };
    }

    private static BehaviorSettings NormalizeBehavior(BehaviorSettings? behavior, List<string> corrections)
    {
        if (behavior is null)
        {
            corrections.Add("behavior: missing, defaults used");
            return new BehaviorSettings();
        }

        var delay = behavior.HoverPreviewDelayMs;
        if (delay is < MinHoverPreviewDelayMs or > MaxHoverPreviewDelayMs)
        {
            corrections.Add("behavior.hoverPreviewDelayMs: out of range, clamped");
            delay = Math.Clamp(delay, MinHoverPreviewDelayMs, MaxHoverPreviewDelayMs);
        }

        return behavior with { HoverPreviewDelayMs = delay, FullscreenExceptions = NormalizeExceptions(behavior.FullscreenExceptions, corrections) };
    }

    /// <summary>Executable file names only (no paths), trimmed, lower-cased, unique, at most <see cref="MaxFullscreenExceptions"/>.</summary>
    private static string[] NormalizeExceptions(IReadOnlyList<string?>? exceptions, List<string> corrections)
    {
        if (exceptions is null)
        {
            return [];
        }

        var invalid = Path.GetInvalidFileNameChars();
        var valid = new List<string>();
        foreach (var raw in exceptions)
        {
            var name = raw?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(name) || name.Length > MaxExecutableNameLength || name.IndexOfAny(invalid) >= 0 || !name.EndsWith(".exe", StringComparison.Ordinal))
            {
                corrections.Add("behavior.fullscreenExceptions: invalid entry removed");
                continue;
            }

            if (valid.Contains(name, StringComparer.Ordinal))
            {
                corrections.Add("behavior.fullscreenExceptions: duplicate entry removed");
                continue;
            }

            if (valid.Count == MaxFullscreenExceptions)
            {
                corrections.Add("behavior.fullscreenExceptions: too many entries, extra removed");
                break;
            }

            valid.Add(name);
        }

        return [.. valid];
    }

    private static UpdateSettings NormalizeUpdates(UpdateSettings? updates, List<string> corrections)
    {
        var defaults = new UpdateSettings();
        if (updates is null)
        {
            corrections.Add("updates: missing, defaults used");
            return defaults;
        }

        return new UpdateSettings
        {
            AutomaticChecks = Defined(updates.AutomaticChecks, defaults.AutomaticChecks, "updates.automaticChecks", corrections),
        };
    }

    private static WidgetSetting[] NormalizeWidgets(IReadOnlyList<WidgetSetting?>? widgets, List<string> corrections)
    {
        var result = new List<WidgetSetting>(WidgetIds.All.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (widgets is null)
        {
            corrections.Add("widgets: missing, defaults used");
        }
        else
        {
            foreach (var widget in widgets)
            {
                if (widget is null || !WidgetIds.IsKnown(widget.Id))
                {
                    corrections.Add("widgets: unknown or invalid entry removed");
                    continue;
                }

                if (!seen.Add(widget.Id))
                {
                    corrections.Add("widgets: duplicate entry removed");
                    continue;
                }

                result.Add(widget);
            }
        }

        foreach (var id in WidgetIds.All.Where(id => !seen.Contains(id)))
        {
            if (widgets is not null)
            {
                corrections.Add($"widgets: missing '{id}' added");
            }

            result.Add(new WidgetSetting(id, true));
        }

        return [.. result];
    }

    private static T Defined<T>(T value, T fallback, string name, List<string> corrections)
        where T : struct, Enum
    {
        if (Enum.IsDefined(value))
        {
            return value;
        }

        corrections.Add($"{name}: unknown value, default used");
        return fallback;
    }

    private static double Clamp(double value, double min, double max, double fallback, string name, List<string> corrections)
    {
        if (!double.IsFinite(value))
        {
            corrections.Add($"{name}: not a number, default used");
            return fallback;
        }

        if (value < min || value > max)
        {
            corrections.Add($"{name}: out of range, clamped");
            return Math.Clamp(value, min, max);
        }

        return value;
    }
}
