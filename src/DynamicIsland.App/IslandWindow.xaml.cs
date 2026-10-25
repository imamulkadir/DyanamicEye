using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Core.Display;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using DynamicIsland.Platform.Windows.Display;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DynamicIsland.App;

/// <summary>
/// Floating island shell. Code-behind is limited to platform interaction: tool-window style, physical-pixel placement,
/// display-change reactions, forwarding pointer/keyboard input to <see cref="IIslandStateMachine"/> and rendering its state.
/// <para>The window always has the expanded size; the visible notch inside grows down from the top edge between state sizes. Transparent
/// pixels are click-through, so the unused area never blocks other apps (ADR 0009). Hidden states hide the window.</para>
/// </summary>
public sealed partial class IslandWindow : Window, IDisposable
{
    public static readonly TimeSpan DisplayChangeQuietPeriod = TimeSpan.FromMilliseconds(250);
    public static readonly Duration TransitionDuration = new(TimeSpan.FromMilliseconds(160));

    private const int WmActivate = 0x0006;
    private const int WmDisplayChange = 0x007E;
    private const int WmSettingChange = 0x001A;
    private const int SpiSetWorkArea = 0x002F;

    private readonly AppSettingsSnapshot _settings;
    private readonly ILogger<IslandWindow> _logger;
    private readonly IIslandStateMachine _state;
    private readonly IslandPlacementController _placement;
    private readonly Debouncer _displayChanges;
    private IntPtr _handle;
    private IntPtr _previousForeground;

    public IslandWindow(
        IMonitorService monitors,
        AppSettingsSnapshot settings,
        IIslandStateMachine state,
        TimeProvider timeProvider,
        ILogger<IslandWindow> logger)
    {
        _settings = settings;
        _logger = logger;
        _state = state;
        _placement = new IslandPlacementController(monitors, () => BuildRequest(_settings.Current));
        _displayChanges = new Debouncer(DisplayChangeQuietPeriod, timeProvider, () => Dispatcher.BeginInvoke(Reposition));
        InitializeComponent();

        Pill.StateMachine = state;
        Pill.MouseEnter += (_, _) => _state.Fire(IslandInput.PointerEnter);
        Pill.MouseLeave += (_, _) => _state.Fire(IslandInput.PointerLeave);
        Pill.MouseLeftButtonUp += (_, _) => _state.Fire(IslandInput.Click);
        PinButton.Click += (_, _) => _state.Fire(IslandInput.TogglePin);
        PreviewKeyDown += OnPreviewKeyDown;
        _state.StateChanged += (_, e) => ApplyState(e.Previous, e.Current, animate: true);

        SourceInitialized += OnSourceInitialized;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, Reposition);
        Closed += OnClosed;
    }

    /// <summary>Last placement, for diagnostics and tests.</summary>
    public PlacementResult? LastPlacement => _placement.Current;

    /// <summary>Window placement request: always the largest island size (the pill inside is aligned per placement).</summary>
    public static PlacementRequest BuildRequest(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var appearance = settings.Appearance;
        return new PlacementRequest(
            settings.General.PreferredMonitorId,
            appearance.Placement,
            appearance.HorizontalOffset,
            appearance.VerticalOffset,
            IslandLayout.Max.Width * appearance.Scale,
            IslandLayout.Max.Height * appearance.Scale);
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

        var appearance = _settings.Current.Appearance;
        Opacity = appearance.Opacity;
        Pill.HorizontalAlignment = appearance.Placement switch
        {
            IslandPlacement.TopLeft => HorizontalAlignment.Left,
            IslandPlacement.TopRight => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };
        IslandWindowInterop.SetBounds(_handle, update.Result.Rect);

        if (update.Changed)
        {
            var rect = update.Result.Rect;
            AppLog.IslandPlaced(_logger, update.Result.Monitor.IsPrimary, update.Result.UsedFallback, update.Result.Monitor.DpiScale, rect.Width, rect.Height);
        }
    }

    /// <summary>Releases the display-change timer; also called on <see cref="Window.Closed"/>.</summary>
    public void Dispose() => _displayChanges.Dispose();

    private void ApplyState(IslandState previous, IslandState current, bool animate)
    {
        var size = IslandLayout.For(current);
        if (size is null)
        {
            Hide();
            return;
        }

        if (!IsVisible)
        {
            Show();
            Reposition();
        }

        var scale = _settings.Current.Appearance.Scale;
        var (width, height) = (size.Value.Width * scale, size.Value.Height * scale);
        var expanded = current is IslandState.Expanded or IslandState.Pinned;

        Animate(Pill, WidthProperty, width, animate);
        Animate(Pill, HeightProperty, height, animate);
        Pill.BottomRadius = Math.Min(height / 2, 22 * scale);
        ExpandedPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        PinButton.IsChecked = current == IslandState.Pinned;
        AutomationPropertiesHelper.SetStateName(Pill, current);

        if (expanded && IsActive)
        {
            Pill.Focus();
        }
        else if (current == IslandState.Collapsed && previous is IslandState.Expanded or IslandState.Pinned && IsActive)
        {
            // Hand focus back to the app the user came from; Windows decides, nothing is forced (§4.1).
            IslandWindowInterop.TryActivate(_previousForeground);
        }
    }

    private void Animate(FrameworkElement element, DependencyProperty property, double to, bool animate)
    {
        if (animate && MotionEnabled())
        {
            var animation = new DoubleAnimation(to, TransitionDuration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            element.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            element.BeginAnimation(property, null);
            element.SetValue(property, to);
        }
    }

    /// <summary>D08/D10: user preference first; "System" follows the Windows animation-effects setting.</summary>
    private bool MotionEnabled() => _settings.Current.Appearance.Motion switch
    {
        MotionPreference.Full => true,
        MotionPreference.Reduced or MotionPreference.Off => false,
        _ => SystemParameters.ClientAreaAnimation,
    };

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _state.Fire(IslandInput.Escape);
                e.Handled = true;
                break;
            case Key.Enter or Key.Space when _state.State is IslandState.Collapsed or IslandState.Peek && Pill.IsKeyboardFocused:
                _state.Fire(IslandInput.Click);
                e.Handled = true;
                break;
            case Key.P when Keyboard.Modifiers == ModifierKeys.Control:
                _state.Fire(IslandInput.TogglePin);
                e.Handled = true;
                break;
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        IslandWindowInterop.MakeToolWindow(_handle);
        HwndSource.FromHwnd(_handle)?.AddHook(WndProc);
        SystemEvents.DisplaySettingsChanged += OnSystemDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Reposition();
        ApplyState(_state.State, _state.State, animate: false);
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
        if (message == WmActivate && (wParam.ToInt64() & 0xFFFF) != 0 && lParam != hwnd)
        {
            // Being activated: lParam is the window losing activation; return focus there on collapse.
            _previousForeground = lParam;
        }
        else if (message == WmDisplayChange || (message == WmSettingChange && wParam == SpiSetWorkArea))
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

    private static class AutomationPropertiesHelper
    {
        public static void SetStateName(UIElement element, IslandState state) =>
            System.Windows.Automation.AutomationProperties.SetName(element, state switch
            {
                IslandState.Peek => "Dynamic Island, preview",
                IslandState.Expanded => "Dynamic Island, expanded",
                IslandState.Pinned => "Dynamic Island, expanded and pinned",
                _ => "Dynamic Island, collapsed",
            });
    }
}
