using System.Windows.Interop;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using DynamicIsland.Platform.Windows.Input;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App;

public enum ShortcutState
{
    Off,
    Active,

    /// <summary>Windows refused: another app or Windows itself uses the combination.</summary>
    InUse,
    Failed,
}

/// <summary>
/// The optional global shortcut (guide P03-05, ADR 0017): registered with Windows only while turned on, re-registered
/// when the setting changes, and mapped to <see cref="ShortcutAction"/>. Owns a message-only window for
/// <c>WM_HOTKEY</c>. UI thread only.
/// </summary>
public sealed class ShortcutController : IDisposable
{
    public const string MessageWindowTitle = "DynamicIsland.Shortcut";
    private const int HotkeyId = 1;

    private readonly IslandWindow _island;
    private readonly IIslandStateMachine _state;
    private readonly ISettingsService _settings;
    private readonly ILogger<ShortcutController> _logger;
    private readonly HwndSource _messageWindow;
    private readonly Win32Hotkey _hotkey;
    private ShortcutSettings? _applied;

    public ShortcutController(IslandWindow island, IIslandStateMachine state, ISettingsService settings, ILogger<ShortcutController> logger)
    {
        _island = island;
        _state = state;
        _settings = settings;
        _logger = logger;
        _messageWindow = new HwndSource(new HwndSourceParameters(MessageWindowTitle)
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE: never shown, receives posted messages such as WM_HOTKEY.
        });
        _messageWindow.AddHook(WndProc);
        _hotkey = new Win32Hotkey(_messageWindow.Handle, HotkeyId);
    }

    /// <summary>Raised on the UI thread after the registration changed.</summary>
    public event EventHandler? StateChanged;

    public ShortcutState State { get; private set; }

    public void Start()
    {
        _settings.Changed += OnSettingsChanged;
        Apply();
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _hotkey.Dispose();
        _messageWindow.Dispose();
    }

    /// <summary>Runs the shortcut action, as a <c>WM_HOTKEY</c> would (tests and diagnostics).</summary>
    public void Press()
    {
        foreach (var input in ShortcutAction.For(_state.State))
        {
            if (input == IslandInput.Click)
            {
                // The process that receives WM_HOTKEY may take the foreground: focus lands on the island when it opens.
                _island.Activate();
            }

            _state.Fire(input);
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) => _messageWindow.Dispatcher.BeginInvoke(() =>
    {
        if (!Equals(settings.Behavior.Shortcut, _applied))
        {
            Apply();
        }
    });

    private void Apply()
    {
        var shortcut = _settings.Current.Behavior.Shortcut;
        _applied = shortcut;

        if (!shortcut.Enabled || !Hotkey.TryParse(shortcut.Keys, out var hotkey, out _))
        {
            _hotkey.Unregister();
            SetState(ShortcutState.Off);
            return;
        }

        var result = _hotkey.Register(hotkey!);
        if (result == HotkeyRegistration.Registered)
        {
            AppLog.ShortcutRegistered(_logger);
            SetState(ShortcutState.Active);
        }
        else
        {
            AppLog.ShortcutUnavailable(_logger, result, _hotkey.LastError);
            SetState(result == HotkeyRegistration.InUse ? ShortcutState.InUse : ShortcutState.Failed);
        }
    }

    private void SetState(ShortcutState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_hotkey.IsPressed(message, wParam))
        {
            Press();
            handled = true;
        }

        return IntPtr.Zero;
    }
}
