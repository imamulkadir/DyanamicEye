using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using DynamicIsland.Core.Display;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using DynamicIsland.Platform.Windows.Tray;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App;

/// <summary>
/// Notification-area icon and menu (guide P03-01/02, ADRs 0012/0013): left click or Enter toggles the island; right click,
/// Shift+F10 or the Menu key opens the accessible menu built from <see cref="TrayMenuModel"/>. Owns a hidden top-level
/// message window (shell callbacks, <c>TaskbarCreated</c> after an Explorer restart). Removed on <see cref="Dispose"/>.
/// </summary>
public sealed class TrayController : IDisposable
{
    public const string MessageWindowTitle = "DynamicIsland.Tray";
    public const uint IconId = 1;

    private const int WsExToolWindow = 0x00000080;
    private const int WmClose = 0x0010;

    private readonly IIslandStateMachine _state;
    private readonly LifecycleCoordinator _lifecycle;
    private readonly LaunchArguments _launch;
    private readonly ISettingsService _settings;
    private readonly IMonitorService _monitors;
    private readonly Func<string?> _islandMonitorId;
    private readonly RestartService _restart;
    private readonly string _logsFolder;
    private readonly ILogger<TrayController> _logger;
    private readonly HwndSource _messageWindow;
    private readonly Win32TrayIcon _icon;
    private readonly ContextMenu _menu;

    public TrayController(
        IIslandStateMachine state,
        LifecycleCoordinator lifecycle,
        LaunchArguments launch,
        ISettingsService settings,
        IMonitorService monitors,
        Func<string?> islandMonitorId,
        RestartService restart,
        string logsFolder,
        ILogger<TrayController> logger)
    {
        _state = state;
        _lifecycle = lifecycle;
        _launch = launch;
        _settings = settings;
        _monitors = monitors;
        _islandMonitorId = islandMonitorId;
        _restart = restart;
        _logsFolder = logsFolder;
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
        _menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        AutomationProperties.SetName(_menu, "Dynamic Island menu");
        AutomationProperties.SetAutomationId(_menu, "Tray.Menu");
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
                Execute(new TrayMenuItem(TrayCommand.ToggleVisibility, string.Empty));
                handled = true;
                break;
            case TrayIconEvent.ContextMenu:
                ShowMenu();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        var model = TrayMenuModel.Build(_state.State, _launch.SafeMode, _monitors.GetMonitors(), _islandMonitorId());
        _menu.Items.Clear();
        foreach (var item in model)
        {
            _menu.Items.Add(CreateMenuItem(item));
        }

        _icon.PrepareForMenu();
        _menu.IsOpen = true;
        (_menu.Items.OfType<MenuItem>().FirstOrDefault(i => i.IsEnabled))?.Focus();
    }

    private Control CreateMenuItem(TrayMenuItem item)
    {
        if (item.Command == TrayCommand.Separator)
        {
            return new Separator();
        }

        var menuItem = new MenuItem
        {
            Header = item.Label,
            IsEnabled = item.Enabled,
            IsCheckable = false,
            IsChecked = item.Checked == true,
        };
        if (item.AutomationId is not null)
        {
            AutomationProperties.SetAutomationId(menuItem, item.AutomationId);
        }

        if (item.ToolTip is not null)
        {
            menuItem.ToolTip = item.ToolTip;
            ToolTipService.SetShowOnDisabled(menuItem, true);
        }

        if (item.Children is { } children)
        {
            foreach (var child in children)
            {
                menuItem.Items.Add(CreateMenuItem(child));
            }
        }
        else
        {
            menuItem.Click += (_, _) => Execute(item);
        }

        return menuItem;
    }

    private void Execute(TrayMenuItem item)
    {
        switch (item.Command)
        {
            case TrayCommand.ToggleVisibility:
                _state.Fire(_state.State switch
                {
                    IslandState.Hidden => IslandInput.Show,
                    IslandState.Paused => IslandInput.Resume,
                    _ => IslandInput.Hide,
                });
                break;
            case TrayCommand.TogglePin:
                _state.Fire(IslandInput.TogglePin);
                break;
            case TrayCommand.TogglePause:
                _state.Fire(_state.State == IslandState.Paused ? IslandInput.Resume : IslandInput.Pause);
                break;
            case TrayCommand.SelectDisplay:
                UpdateSettings(s => s with { General = s.General with { PreferredMonitorId = item.Argument } });
                break;
            case TrayCommand.ResetDisplay:
                // Also the recovery for an island that ended up on a wrong or missing screen (G03).
                var defaults = new AppearanceSettings();
                UpdateSettings(s => s with
                {
                    General = s.General with { PreferredMonitorId = null },
                    Appearance = s.Appearance with { HorizontalOffset = defaults.HorizontalOffset, VerticalOffset = defaults.VerticalOffset },
                });
                break;
            case TrayCommand.OpenLogs:
                OpenLogsFolder();
                break;
            case TrayCommand.ToggleSafeMode:
                _restart.Restart(safeMode: !_launch.SafeMode);
                break;
            case TrayCommand.Restart:
                _restart.Restart(safeMode: _launch.SafeMode);
                break;
            case TrayCommand.Exit:
                _lifecycle.RequestShutdown(ExitReason.UserExit);
                break;
        }
    }

    /// <summary>Applies a settings change; a failing save is logged (by type) and never crashes the app.</summary>
    private async void UpdateSettings(Func<AppSettings, AppSettings> change)
    {
        try
        {
            if (await _settings.UpdateAsync(change, _lifecycle.ApplicationStopping) == SettingsSaveResult.SkippedReadOnly)
            {
                AppLog.SettingsNotSaved(_logger, "read-only");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            AppLog.SettingsNotSaved(_logger, ex.GetType().Name);
        }
    }

    private void OpenLogsFolder()
    {
        try
        {
            Directory.CreateDirectory(_logsFolder);
            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            startInfo.ArgumentList.Add(_logsFolder);
            Process.Start(startInfo)?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            AppLog.OpenLogsFailed(_logger, ex.GetType().Name);
        }
    }
}
