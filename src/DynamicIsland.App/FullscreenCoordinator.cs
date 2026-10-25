using System.Windows.Threading;
using DynamicIsland.Core.Display;
using DynamicIsland.Core.Shell;
using DynamicIsland.Platform.Windows.Display;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App;

/// <summary>
/// Connects fullscreen detection to the island (D06): detector changes are debounced (<see cref="QuietPeriod"/>, so
/// alt-tabbing does not make the island flicker), checked against <see cref="FullscreenPolicy"/> for the island's monitor
/// and current settings, and turned into <see cref="IslandInput.FullscreenStarted"/>/<see cref="IslandInput.FullscreenEnded"/>.
/// Logs only "hidden/visible" — never window titles, process names or monitor ids.
/// </summary>
public sealed class FullscreenCoordinator : IDisposable
{
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(300);

    private readonly Win32FullscreenDetector _detector;
    private readonly IIslandStateMachine _state;
    private readonly AppSettingsSnapshot _settings;
    private readonly Func<string?> _islandMonitorId;
    private readonly ILogger<FullscreenCoordinator> _logger;
    private readonly Debouncer _debouncer;
    private bool _hidden;

    public FullscreenCoordinator(
        Win32FullscreenDetector detector,
        IIslandStateMachine state,
        AppSettingsSnapshot settings,
        Func<string?> islandMonitorId,
        TimeProvider timeProvider,
        Dispatcher dispatcher,
        ILogger<FullscreenCoordinator> logger)
    {
        _detector = detector;
        _state = state;
        _settings = settings;
        _islandMonitorId = islandMonitorId;
        _logger = logger;
        _debouncer = new Debouncer(QuietPeriod, timeProvider, () => dispatcher.BeginInvoke(Apply));
        _detector.Changed += (_, _) => _debouncer.Trigger();
    }

    /// <summary>Starts detection; call on the UI thread after the island window exists.</summary>
    public void Start()
    {
        _detector.Start();
        AppLog.FullscreenDetectionStarted(_logger, _detector.ActiveHookCount);
        Apply();
    }

    public void Dispose()
    {
        _debouncer.Dispose();
        _detector.Dispose();
    }

    private void Apply()
    {
        var hide = FullscreenPolicy.ShouldHide(_detector.Current, _islandMonitorId(), _settings.Current.Behavior);
        if (hide == _hidden)
        {
            return;
        }

        _hidden = hide;
        _state.Fire(hide ? IslandInput.FullscreenStarted : IslandInput.FullscreenEnded);
        AppLog.FullscreenHide(_logger, hide);
    }
}
