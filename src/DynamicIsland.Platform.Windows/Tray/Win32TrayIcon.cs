namespace DynamicIsland.Platform.Windows.Tray;

public enum TrayIconEvent
{
    None,

    /// <summary>Left click (or touch) on the icon.</summary>
    Select,

    /// <summary>Enter/Space while the icon has keyboard focus in the notification area.</summary>
    KeySelect,

    /// <summary>Right click, Shift+F10 or the Menu key: show the context menu.</summary>
    ContextMenu,
}

/// <summary>
/// Notification-area icon over <c>Shell_NotifyIcon</c> (version 4: keyboard and accessibility events). The owner window
/// forwards its messages to <see cref="HandleMessage"/>; the icon re-registers itself when Explorer restarts
/// (<c>TaskbarCreated</c> broadcast, which only reaches top-level windows) and is removed on <see cref="Dispose"/>.
/// </summary>
public sealed unsafe class Win32TrayIcon : IDisposable
{
    /// <summary><c>WM_APP + 0x55</c>: callback message the shell sends to the owner window.</summary>
    public const int CallbackMessage = 0x8000 + 0x55;

    private const int NinSelect = 0x0400;
    private const int NinKeySelect = 0x0401;
    private const int WmContextMenu = 0x007B;
    private const int MaxTooltipLength = 127;

    private static readonly uint TaskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

    private readonly IntPtr _window;
    private readonly uint _id;
    private string _tooltip;
    private IntPtr _icon;
    private bool _ownsIcon;

    /// <param name="window">Visible-to-the-shell top-level owner window (may be hidden, but not message-only).</param>
    public Win32TrayIcon(IntPtr window, uint id, string tooltip)
    {
        _window = window;
        _id = id;
        _tooltip = Truncate(tooltip);
    }

    /// <summary><c>false</c> when there is no shell notification area (e.g. Server Core, some CI desktops).</summary>
    public static bool IsShellTrayAvailable => NativeMethods.FindWindow("Shell_TrayWnd", null) != IntPtr.Zero;

    public bool IsAdded { get; private set; }

    /// <summary>Registers the icon with the shell. Safe to call again (e.g. after Explorer restarted).</summary>
    public bool Add()
    {
        EnsureIcon();
        var data = CreateData(NativeMethods.NifMessage | NativeMethods.NifIcon | NativeMethods.NifTip | NativeMethods.NifShowTip);
        IsAdded = NativeMethods.ShellNotifyIcon(NativeMethods.NimAdd, &data);
        if (IsAdded)
        {
            data.TimeoutOrVersion = NativeMethods.NotifyIconVersion4;
            NativeMethods.ShellNotifyIcon(NativeMethods.NimSetVersion, &data);
        }

        return IsAdded;
    }

    public void SetTooltip(string tooltip)
    {
        _tooltip = Truncate(tooltip);
        if (IsAdded)
        {
            var data = CreateData(NativeMethods.NifTip | NativeMethods.NifShowTip);
            NativeMethods.ShellNotifyIcon(NativeMethods.NimModify, &data);
        }
    }

    public void Remove()
    {
        var data = CreateData(0);
        NativeMethods.ShellNotifyIcon(NativeMethods.NimDelete, &data);
        IsAdded = false;
    }

    /// <summary>Whether the shell currently knows this icon (it may sit in the overflow area).</summary>
    public bool IsRegisteredWithShell() => IsRegistered(_window, _id);

    /// <summary>Whether the shell knows the icon <paramref name="id"/> of <paramref name="window"/> (any process).</summary>
    public static bool IsRegistered(IntPtr window, uint id)
    {
        var identifier = new NativeMethods.NotifyIconIdentifier
        {
            Size = (uint)sizeof(NativeMethods.NotifyIconIdentifier),
            Window = window,
            Id = id,
        };
        NativeMethods.Rect rect;
        return NativeMethods.ShellNotifyIconGetRect(&identifier, &rect) == 0;
    }

    /// <summary>
    /// Lets the owner window take the foreground right before a tray menu opens, so the menu closes when the user clicks
    /// elsewhere (documented <c>Shell_NotifyIcon</c> requirement). Allowed because the shell just delivered user input.
    /// </summary>
    public void PrepareForMenu() => NativeMethods.SetForegroundWindow(_window);

    /// <summary>Interprets an owner-window message; re-adds the icon after an Explorer restart.</summary>
    public TrayIconEvent HandleMessage(int message, IntPtr wParam, IntPtr lParam)
    {
        if (TaskbarCreatedMessage != 0 && (uint)message == TaskbarCreatedMessage)
        {
            IsAdded = false;
            Add();
            return TrayIconEvent.None;
        }

        if (message != CallbackMessage)
        {
            return TrayIconEvent.None;
        }

        return (int)(lParam.ToInt64() & 0xFFFF) switch
        {
            NinSelect => TrayIconEvent.Select,
            NinKeySelect => TrayIconEvent.KeySelect,
            WmContextMenu => TrayIconEvent.ContextMenu,
            _ => TrayIconEvent.None,
        };
    }

    /// <summary>The message id the shell broadcasts when the taskbar is (re)created.</summary>
    public static uint TaskbarCreated => TaskbarCreatedMessage;

    /// <summary>Delivers the Explorer-restart notification to one window (tests simulate an Explorer restart with it).</summary>
    public static void SendTaskbarCreated(IntPtr window) => NativeMethods.SendMessage(window, TaskbarCreatedMessage, IntPtr.Zero, IntPtr.Zero);

    public void Dispose()
    {
        Remove();
        if (_ownsIcon && _icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
        }

        _icon = IntPtr.Zero;
    }

    /// <summary>
    /// Loads the exe's own icon (group 32512, the app icon) at the small-icon size for the owner window's DPI, so it is
    /// crisp at any scaling; falls back to the shared system application icon.
    /// </summary>
    private void EnsureIcon()
    {
        if (_icon != IntPtr.Zero)
        {
            return;
        }

        var dpi = NativeMethods.GetDpiForWindow(_window);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var cx = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxSmIcon, dpi);
        var cy = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCySmIcon, dpi);
        _icon = NativeMethods.LoadImage(NativeMethods.GetModuleHandle(null), NativeMethods.IdiApplication, NativeMethods.ImageIcon, cx, cy, 0);
        _ownsIcon = _icon != IntPtr.Zero;
        if (!_ownsIcon)
        {
            _icon = NativeMethods.LoadImage(IntPtr.Zero, NativeMethods.IdiApplication, NativeMethods.ImageIcon, cx, cy, NativeMethods.LrShared);
        }
    }

    private NativeMethods.NotifyIconData CreateData(uint flags)
    {
        var data = new NativeMethods.NotifyIconData
        {
            Size = (uint)sizeof(NativeMethods.NotifyIconData),
            Window = _window,
            Id = _id,
            Flags = flags,
            CallbackMessage = CallbackMessage,
            Icon = _icon,
        };

        var tip = _tooltip.AsSpan();
        for (var i = 0; i < tip.Length; i++)
        {
            data.Tip[i] = tip[i];
        }

        data.Tip[tip.Length] = '\0';
        return data;
    }

    private static string Truncate(string tooltip) =>
        tooltip.Length <= MaxTooltipLength ? tooltip : tooltip[..MaxTooltipLength];
}
