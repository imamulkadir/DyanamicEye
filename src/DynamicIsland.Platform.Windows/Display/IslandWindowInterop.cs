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

    public static bool IsVisible(IntPtr window) => NativeMethods.IsWindowVisible(window);

    /// <summary>Top-level window with this exact title owned by <paramref name="processId"/> (hidden windows included).</summary>
    public static IntPtr FindTopLevelWindow(int processId, string title)
    {
        var current = IntPtr.Zero;
        while ((current = NativeMethods.FindWindowEx(IntPtr.Zero, current, null, title)) != IntPtr.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(current, out var owner);
            if (owner == (uint)processId)
            {
                return current;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>Message-only window (<c>HWND_MESSAGE</c> child) with this exact title owned by <paramref name="processId"/>.</summary>
    public static IntPtr FindMessageWindow(int processId, string title)
    {
        var messageParent = new IntPtr(-3);
        var current = IntPtr.Zero;
        while ((current = NativeMethods.FindWindowEx(messageParent, current, null, title)) != IntPtr.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(current, out var owner);
            if (owner == (uint)processId)
            {
                return current;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>Posts a message (tests: simulate shell or system notifications without input injection).</summary>
    public static bool Post(IntPtr window, int message, IntPtr wParam, IntPtr lParam) => NativeMethods.PostMessage(window, (uint)message, wParam, lParam);

    /// <summary>Posts <c>WM_CLOSE</c>, as tools such as taskkill (without /F) do.</summary>
    public static void RequestClose(IntPtr window) => NativeMethods.PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);

    public static bool IsForeground(IntPtr window) => window != IntPtr.Zero && NativeMethods.GetForegroundWindow() == window;

    /// <summary>
    /// Gives focus back to <paramref name="window"/> (the window that was active before the island) when it still exists.
    /// Only works while the island is foreground; Windows refuses otherwise, which is the intended "no force" behaviour.
    /// </summary>
    public static bool TryActivate(IntPtr window) =>
        window != IntPtr.Zero && NativeMethods.IsWindow(window) && NativeMethods.IsWindowVisible(window) && NativeMethods.SetForegroundWindow(window);

    public static bool IsTopmost(IntPtr window) =>
        (NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlExStyle) & NativeMethods.WsExTopmost) != 0;

    /// <summary>Moves/sizes in physical pixels without activating the window; makes it topmost unless told otherwise.</summary>
    public static void SetBounds(IntPtr window, PixelRect rect, bool topmost = true)
    {
        var flags = NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder | (topmost ? 0 : NativeMethods.SwpNoZOrder);
        if (!NativeMethods.SetWindowPos(window, topmost ? NativeMethods.HwndTopmost : IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height, flags))
        {
            throw new System.ComponentModel.Win32Exception();
        }
    }
}
