using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DynamicIsland.Core.Display;
using DynamicIsland.Core.Settings;
using DynamicIsland.Platform.Windows.Display;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DynamicIsland.App;

/// <summary>
/// Floating island shell. Code-behind is limited to platform interaction: tool-window style, physical-pixel placement
/// and reacting to display changes. Placement decisions live in <see cref="IslandPlacementController"/>.
/// <para>Re-placement triggers: DPI change (immediate), and — coalesced over <see cref="DisplayChangeQuietPeriod"/> —
/// display/resolution change, work-area (taskbar) change, system display-settings change and resume from sleep.</para>
/// </summary>
public sealed partial class IslandWindow : Window, IDisposable
{
    /// <summary>Collapsed pill size in DIPs before the user's appearance scale.</summary>
    public const double CollapsedWidth = 200;
    public const double CollapsedHeight = 36;

    public static readonly TimeSpan DisplayChangeQuietPeriod = TimeSpan.FromMilliseconds(250);

    private const int WmDisplayChange = 0x007E;
    private const int WmSettingChange = 0x001A;
    private const int SpiSetWorkArea = 0x002F;

    private readonly AppSettingsSnapshot _settings;
    private readonly ILogger<IslandWindow> _logger;
    private readonly IslandPlacementController _placement;
    private readonly Debouncer _displayChanges;
    private IntPtr _handle;

    public IslandWindow(IMonitorService monitors, AppSettingsSnapshot settings, TimeProvider timeProvider, ILogger<IslandWindow> logger)
    {
        _settings = settings;
        _logger = logger;
        _placement = new IslandPlacementController(monitors, () => BuildRequest(_settings.Current));
        _displayChanges = new Debouncer(DisplayChangeQuietPeriod, timeProvider, () => Dispatcher.BeginInvoke(Reposition));
        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, Reposition);
        Closed += OnClosed;
    }

    /// <summary>Last placement, for diagnostics and tests.</summary>
    public PlacementResult? LastPlacement => _placement.Current;

    public static PlacementRequest BuildRequest(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var appearance = settings.Appearance;
        return new PlacementRequest(
            settings.General.PreferredMonitorId,
            appearance.Placement,
            appearance.HorizontalOffset,
            appearance.VerticalOffset,
            CollapsedWidth * appearance.Scale,
            CollapsedHeight * appearance.Scale);
    }

    /// <summary>Recomputes the target monitor and rectangle from current settings and displays.</summary>
    public void Reposition()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        var update = _placement.Update();
        if (update is null)
        {
            return;
        }

        Opacity = _settings.Current.Appearance.Opacity;
        IslandWindowInterop.SetBounds(_handle, update.Result.Rect);

        if (update.Changed)
        {
            var rect = update.Result.Rect;
            AppLog.IslandPlaced(_logger, update.Result.Monitor.IsPrimary, update.Result.UsedFallback, update.Result.Monitor.DpiScale, rect.Width, rect.Height);
        }
    }

    /// <summary>Releases the display-change timer; also called on <see cref="Window.Closed"/>.</summary>
    public void Dispose() => _displayChanges.Dispose();

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        IslandWindowInterop.MakeToolWindow(_handle);
        HwndSource.FromHwnd(_handle)?.AddHook(WndProc);
        SystemEvents.DisplaySettingsChanged += OnSystemDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Reposition();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // SystemEvents are static: always unsubscribe to avoid leaking the window.
        SystemEvents.DisplaySettingsChanged -= OnSystemDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _displayChanges.Dispose();
        _handle = IntPtr.Zero;
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmDisplayChange || (message == WmSettingChange && wParam == SpiSetWorkArea))
        {
            _displayChanges.Trigger();
        }

        return IntPtr.Zero;
    }

    private void OnSystemDisplaySettingsChanged(object? sender, EventArgs e) => _displayChanges.Trigger();

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _displayChanges.Trigger();
        }
    }
}
