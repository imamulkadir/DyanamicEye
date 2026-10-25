using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Shell;
using DynamicIsland.Platform.Windows.Tray;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App;

/// <summary>
/// Notification-area icon (guide P03-01): left click or Enter toggles the island; right click, Shift+F10 or the Menu key
/// opens an accessible WPF menu. Owns a hidden top-level message window (needed for the shell callback and for the
/// <c>TaskbarCreated</c> broadcast after an Explorer restart). Removed on <see cref="Dispose"/> (app exit).
/// The full action list arrives in P03-02.
/// </summary>
public sealed class TrayController : IDisposable
{
    public const string MessageWindowTitle = "DynamicIsland.Tray";
    public const uint IconId = 1;

    private const int WsExToolWindow = 0x00000080;
    private const int WmClose = 0x0010;

    private readonly IIslandStateMachine _state;
    private readonly LifecycleCoordinator _lifecycle;
    private readonly ILogger<TrayController> _logger;
    private readonly HwndSource _messageWindow;
    private readonly Win32TrayIcon _icon;
    private readonly ContextMenu _menu;
    private readonly MenuItem _toggleItem;

    public TrayController(IIslandStateMachine state, LifecycleCoordinator lifecycle, LaunchArguments launch, ILogger<TrayController> logger)
    {
        _state = state;
        _lifecycle = lifecycle;
        _logger = logger;

        _messageWindow = new HwndSource(new HwndSourceParameters(MessageWindowTitle)
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = WsExToolWindow,
        });
        _messageWindow.AddHook(WndProc);

        _icon = new Win32TrayIcon(_messageWindow.Handle, IconId, launch.SafeMode ? "Dynamic Island (Safe Mode)" : "Dynamic Island");

        _toggleItem = new MenuItem();
        _toggleItem.Click += (_, _) => ToggleIsland();
        var exitItem = new MenuItem { Header = "E_xit" };
        exitItem.Click += (_, _) => _lifecycle.RequestShutdown(ExitReason.UserExit);

        _menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        _menu.Items.Add(_toggleItem);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(exitItem);
        System.Windows.Automation.AutomationProperties.SetName(_menu, "Dynamic Island menu");
    }

    /// <summary>Handle of the hidden owner window (diagnostics and tests).</summary>
    public IntPtr MessageWindow => _messageWindow.Handle;

    /// <summary>Adds the icon. Without a shell notification area the app keeps running without a tray icon.</summary>
    public void Start()
    {
        if (!_icon.Add())
        {
            AppLog.TrayUnavailable(_logger);
        }
    }

    public void Dispose()
    {
        _menu.IsOpen = false;
        _icon.Dispose();
        _messageWindow.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmClose)
        {
            // taskkill (without /F) and some tools close every top-level window of the process. Treat that as a normal
            // Exit instead of letting DefWindowProc destroy the tray owner (which would orphan the icon).
            _lifecycle.RequestShutdown(ExitReason.UserExit);
            handled = true;
            return IntPtr.Zero;
        }

        switch (_icon.HandleMessage(message, wParam, lParam))
        {
            case TrayIconEvent.Select or TrayIconEvent.KeySelect:
                ToggleIsland();
                handled = true;
                break;
            case TrayIconEvent.ContextMenu:
                ShowMenu();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>Hidden → show; paused → resume; any other state → hide.</summary>
    private void ToggleIsland() => _state.Fire(_state.State switch
    {
        IslandState.Hidden => IslandInput.Show,
        IslandState.Paused => IslandInput.Resume,
        _ => IslandInput.Hide,
    });

    private void ShowMenu()
    {
        _toggleItem.Header = _state.State is IslandState.Hidden or IslandState.Paused ? "_Show island" : "_Hide island";
        _icon.PrepareForMenu();
        _menu.IsOpen = true;
        _toggleItem.Focus();
    }
}
