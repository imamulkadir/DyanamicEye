using DynamicIsland.Core.Media;
using DynamicIsland.Core.Metrics;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Widgets;

namespace DynamicIsland.Widgets;

/// <summary>
/// The only source of widgets (ADR 0018): compiled in, no reflection or plugin loading. Each widget task (P04-02…P04-06)
/// adds its module here.
/// </summary>
public static class BuiltInWidgets
{
    public static IReadOnlyList<IWidget> Create(ISettingsService settings, TimeProvider timeProvider, IMediaSessionService media, ISystemMetricsService metrics)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(metrics);
        return [new ClockWidget(settings, timeProvider), new MediaWidget(media), new MetricsWidget(metrics, timeProvider)];
    }
}
