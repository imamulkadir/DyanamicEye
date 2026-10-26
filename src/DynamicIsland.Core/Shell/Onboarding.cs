using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Core.Shell;

/// <summary>First-run choices (guide P03-04, ADR 0015 §5).</summary>
public static class Onboarding
{
    /// <summary>Shown while a consent is still unanswered; never in Safe Mode or on a sign-in start.</summary>
    public static bool IsNeeded(AppSettings settings, LaunchArguments launch)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(launch);

        return !launch.SafeMode
            && !launch.Startup
            && (settings.General.StartWithWindows == ConsentState.NotAsked || settings.Updates.AutomaticChecks == ConsentState.NotAsked);
    }

    /// <summary>The choices the user confirmed: an unchecked box is an explicit "no".</summary>
    public static AppSettings Apply(AppSettings latest, bool startWithWindows, bool dailyUpdateChecks, IReadOnlyList<WidgetSetting> widgets)
    {
        ArgumentNullException.ThrowIfNull(latest);
        ArgumentNullException.ThrowIfNull(widgets);

        return latest with
        {
            General = latest.General with { StartWithWindows = startWithWindows ? ConsentState.Granted : ConsentState.Declined },
            Updates = latest.Updates with { AutomaticChecks = dailyUpdateChecks ? ConsentState.Granted : ConsentState.Declined },
            Widgets = [.. widgets],
        };
    }
}
