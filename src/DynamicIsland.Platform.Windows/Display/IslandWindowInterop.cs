using DynamicIsland.Core.Display;

namespace DynamicIsland.Platform.Windows.Display;

/// <summary>Win32 window operations the WPF shell cannot express directly.</summary>
public static class IslandWindowInterop
{
    /// <summary>Tool window: no taskbar button and not listed in Alt+Tab. Focus still works when clicked (§4.1).</summary>
    public static void MakeToolWindow(IntPtr window)
    {
        var style = NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlExStyle);
        var updated = (style | NativeMethods.WsExToolWindow) & ~NativeMethods.WsExAppWindow;
        if (updated != style)
        {
            NativeMethods.SetWindowLongPtr(window, NativeMethods.GwlExStyle, updated);
        }
    }

    public static bool IsToolWindow(IntPtr window) =>
        (NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlExStyle) & NativeMethods.WsExToolWindow) != 0;

    /// <summary>Current window rectangle in physical pixels.</summary>
    public static unsafe PixelRect GetBounds(IntPtr window)
    {
        NativeMethods.Rect rect;
        if (!NativeMethods.GetWindowRect(window, &rect))
        {
            throw new System.ComponentModel.Win32Exception();
        }

        return new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>
    /// Whether a mouse click at a physical screen point would reach <paramref name="window"/>. Transparent pixels of a
    /// layered window are not hit-testable, so clicks there go to the window underneath.
    /// </summary>
    public static bool ReceivesClickAt(IntPtr window, int x, int y)
    {
        var hit = NativeMethods.WindowFromPoint(new NativeMethods.Point { X = x, Y = y });
        return hit != IntPtr.Zero && NativeMethods.GetAncestor(hit, NativeMethods.GaRoot) == window;
    }

    public static bool IsTopmost(IntPtr window) =>
        (NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlExStyle) & NativeMethods.WsExTopmost) != 0;

    /// <summary>Moves/sizes in physical pixels, keeps topmost, never activates the window.</summary>
    public static void SetBounds(IntPtr window, PixelRect rect)
    {
        if (!NativeMethods.SetWindowPos(window, NativeMethods.HwndTopmost, rect.Left, rect.Top, rect.Width, rect.Height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder))
        {
            throw new System.ComponentModel.Win32Exception();
        }
    }
}
