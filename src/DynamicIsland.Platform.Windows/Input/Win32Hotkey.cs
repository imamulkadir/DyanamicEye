using System.Runtime.InteropServices;
using DynamicIsland.Core.Shell;

namespace DynamicIsland.Platform.Windows.Input;

public enum HotkeyRegistration
{
    Registered,

    /// <summary>Another app or Windows owns this combination (<c>ERROR_HOTKEY_ALREADY_REGISTERED</c>).</summary>
    InUse,

    /// <summary>Windows refused for another reason (see <see cref="Win32Hotkey.LastError"/>).</summary>
    Failed,
}

/// <summary>
/// One system-wide shortcut through <c>RegisterHotKey</c> (ADR 0017): no keyboard hook. Windows posts <c>WM_HOTKEY</c>
/// to <paramref name="window"/>, so the owner thread must pump messages. Not thread-safe; use from that thread.
/// </summary>
public sealed class Win32Hotkey(IntPtr window, int id) : IDisposable
{
    public const int WmHotkey = 0x0312;

    private const uint ModNoRepeat = 0x4000;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    /// <summary>The registered shortcut, or <c>null</c>.</summary>
    public Hotkey? Current { get; private set; }

    /// <summary>Win32 error of the last failed registration (diagnostics).</summary>
    public int LastError { get; private set; }

    /// <summary>Replaces any current registration with <paramref name="hotkey"/>.</summary>
    public HotkeyRegistration Register(Hotkey hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);

        Unregister();
        if (NativeMethods.RegisterHotKey(window, id, (uint)hotkey.Modifiers | ModNoRepeat, (uint)hotkey.VirtualKey))
        {
            Current = hotkey;
            LastError = 0;
            return HotkeyRegistration.Registered;
        }

        LastError = Marshal.GetLastPInvokeError();
        return LastError == ErrorHotkeyAlreadyRegistered ? HotkeyRegistration.InUse : HotkeyRegistration.Failed;
    }

    public void Unregister()
    {
        if (Current is not null)
        {
            NativeMethods.UnregisterHotKey(window, id);
            Current = null;
        }
    }

    /// <summary>True for this registration's <c>WM_HOTKEY</c>.</summary>
    public bool IsPressed(int message, IntPtr wParam) => message == WmHotkey && wParam.ToInt64() == id && Current is not null;

    public void Dispose() => Unregister();
}
