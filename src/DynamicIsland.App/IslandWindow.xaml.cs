using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DynamicIsland.Core.Display;
using DynamicIsland.Platform.Windows.Display;

namespace DynamicIsland.App;

/// <summary>
/// Floating island shell. Code-behind is limited to platform interaction: tool-window style, physical-pixel placement
/// and reacting to display, DPI and work-area changes. Placement logic lives in <see cref="IslandPlacementCalculator"/>.
/// </summary>
public partial class IslandWindow : Window
{
    /// <summary>Collapsed pill size in DIPs before the user's appearance scale.</summary>
    public const double CollapsedWidth = 200;
    public const double CollapsedHeight = 36;

    private const int WmDisplayChange = 0x007E;
    private const int WmSettingChange = 0x001A;
    private const int SpiSetWorkArea = 0x002F;

    private readonly IMonitorService _monitors;
    private readonly AppSettingsSnapshot _settings;
    private IntPtr _handle;
    private bool _repositionQueued;

    public IslandWindow(IMonitorService monitors, AppSettingsSnapshot settings)
    {
        _monitors = monitors;
        _settings = settings;
        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        DpiChanged += (_, _) => QueueReposition();
    }

    /// <summary>Last placement, for diagnostics and tests.</summary>
    public PlacementResult? LastPlacement { get; private set; }

    /// <summary>Recomputes the target monitor and rectangle from current settings and displays.</summary>
    public void Reposition()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        var monitors = _monitors.GetMonitors();
        if (monitors.Count == 0)
        {
            return;
        }

        var settings = _settings.Current;
        var appearance = settings.Appearance;
        var request = new PlacementRequest(
            settings.General.PreferredMonitorId,
            appearance.Placement,
            appearance.HorizontalOffset,
            appearance.VerticalOffset,
            CollapsedWidth * appearance.Scale,
            CollapsedHeight * appearance.Scale);

        LastPlacement = IslandPlacementCalculator.Place(monitors, request);
        Opacity = appearance.Opacity;
        IslandWindowInterop.SetBounds(_handle, LastPlacement.Rect);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        IslandWindowInterop.MakeToolWindow(_handle);
        HwndSource.FromHwnd(_handle)?.AddHook(WndProc);
        Reposition();
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmDisplayChange || (message == WmSettingChange && wParam == SpiSetWorkArea))
        {
            QueueReposition();
        }

        return IntPtr.Zero;
    }

    /// <summary>Coalesces bursts of display messages (and WPF's own DPI resize) into one placement pass.</summary>
    private void QueueReposition()
    {
        if (_repositionQueued)
        {
            return;
        }

        _repositionQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _repositionQueued = false;
            Reposition();
        });
    }
}
