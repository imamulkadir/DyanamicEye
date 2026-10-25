using System.Diagnostics;
using System.Runtime.InteropServices;
using DynamicIsland.Core.Display;

namespace DynamicIsland.Platform.Windows.Display;

/// <summary>
/// Event-driven fullscreen detection (D06, D10; no polling). Re-evaluates when the foreground window changes (plus one
/// follow-up check 0.5 s later), is restored, uncloaked, or moves/resizes (out-of-context WinEvent hooks, delivered on the thread that called <see cref="Start"/> — it must pump
/// messages, i.e. the WPF UI thread).
/// <para>A window is fullscreen when it covers its monitor's full bounds and is not simply a maximized captioned window
/// (those leave the taskbar), and is not the shell/desktop, cloaked, minimized or one of this process's windows. Windows'
/// own Direct3D-fullscreen and presentation-mode states also count. Exclusive-fullscreen games may still draw above any
/// overlay; the island only ever hides, it never tries to stay on top of them.</para>
/// </summary>
public sealed unsafe class Win32FullscreenDetector : IFullscreenDetector, IDisposable
{
    private static readonly string[] ShellWindowClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    // WinEvent callbacks are static (UnmanagedCallersOnly); one detector per process routes them.
    private static Win32FullscreenDetector? s_active;

    /// <summary>
    /// One follow-up evaluation after a foreground change: newly shown windows can still be DWM-cloaked (opening
    /// animation) or resizing when the foreground event arrives. Bounded (one shot per change), not polling.
    /// </summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromMilliseconds(500);

    private readonly List<IntPtr> _hooks = [];
    private readonly Lock _gate = new();
    private readonly Timer _followUp;
    private IntPtr _foreground;

    public Win32FullscreenDetector() => _followUp = new Timer(_ => Refresh(), null, Timeout.Infinite, Timeout.Infinite);

    public event EventHandler<FullscreenSnapshot>? Changed;

    public FullscreenSnapshot Current { get; private set; } = FullscreenSnapshot.None;

    /// <summary>Number of WinEvent hooks installed by <see cref="Start"/> (4 expected); logged for diagnostics.</summary>
    public int ActiveHookCount => _hooks.Count;


    /// <summary>Installs the hooks on the calling (UI) thread and evaluates the current foreground window.</summary>
    public void Start()
    {
        if (Interlocked.CompareExchange(ref s_active, this, null) is not null)
        {
            throw new InvalidOperationException("Only one fullscreen detector can be active per process.");
        }

        const uint flags = NativeMethods.WinEventOutOfContext | NativeMethods.WinEventSkipOwnProcess;
        foreach (var (min, max) in new[]
                 {
                     (NativeMethods.EventSystemForeground, NativeMethods.EventSystemForeground),
                     (NativeMethods.EventSystemMinimizeEnd, NativeMethods.EventSystemMinimizeEnd),
                     (NativeMethods.EventObjectLocationChange, NativeMethods.EventObjectLocationChange),
                     (NativeMethods.EventObjectUncloaked, NativeMethods.EventObjectUncloaked),
                 })
        {
            var hook = NativeMethods.SetWinEventHook(min, max, IntPtr.Zero, &OnWinEvent, 0, 0, flags);
            if (hook != IntPtr.Zero)
            {
                _hooks.Add(hook);
            }
        }

        Refresh();
    }

    /// <summary>Re-evaluates now (e.g. after display changes). Thread-safe.</summary>
    public void Refresh()
    {
        lock (_gate)
        {
            _foreground = NativeMethods.GetForegroundWindow();
            Publish(Evaluate(_foreground));
        }
    }

    public void Dispose()
    {
        foreach (var hook in _hooks)
        {
            NativeMethods.UnhookWinEvent(hook);
        }

        _hooks.Clear();
        _followUp.Dispose();
        Interlocked.CompareExchange(ref s_active, null, this);
    }

    /// <summary>Classifies one window (public so it can be verified against real helper windows).</summary>
    /// <param name="ignoreOwnProcess">Skip windows of this process (the island itself); tests evaluate their own helper windows.</param>
    public static FullscreenSnapshot Evaluate(IntPtr window, bool ignoreOwnProcess = true)
    {
        var notification = QueryNotificationState();

        if (!IsCandidate(window, ignoreOwnProcess))
        {
            return notification == FullscreenSource.None ? FullscreenSnapshot.None : new FullscreenSnapshot(true, notification, null, null);
        }

        var monitor = NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest);
        var monitorBounds = Win32MonitorService.BoundsOf(monitor);
        var source = notification;

        if (monitorBounds is { } bounds && CoversMonitor(window, bounds))
        {
            source = FullscreenSource.WindowCoversMonitor;
        }

        return source == FullscreenSource.None
            ? FullscreenSnapshot.None
            : new FullscreenSnapshot(true, source, Win32MonitorService.IdFor(monitor), ExecutableOf(window));
    }

    [UnmanagedCallersOnly]
    private static void OnWinEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        var detector = s_active;
        if (detector is null || objectId != NativeMethods.ObjIdWindow)
        {
            return;
        }

        if (eventType is NativeMethods.EventObjectLocationChange or NativeMethods.EventObjectUncloaked && window != detector._foreground)
        {
            // Location/cloak changes fire for every window; only the foreground one matters.
            return;
        }

        detector.Refresh();
        if (eventType == NativeMethods.EventSystemForeground)
        {
            detector._followUp.Change(FollowUpDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Publish(FullscreenSnapshot snapshot)
    {
        if (snapshot == Current)
        {
            return;
        }

        Current = snapshot;
        Changed?.Invoke(this, snapshot);
    }

    private static bool IsCandidate(IntPtr window, bool ignoreOwnProcess)
    {
        if (window == IntPtr.Zero || window == NativeMethods.GetShellWindow() || !NativeMethods.IsWindowVisible(window) || NativeMethods.IsIconic(window))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (ignoreOwnProcess && processId == (uint)Environment.ProcessId)
        {
            return false;
        }

        if (NativeMethods.DwmGetWindowAttribute(window, NativeMethods.DwmWindowAttributeCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }

        var className = stackalloc char[64];
        var length = NativeMethods.GetClassName(window, className, 64);
        var name = new string(className, 0, Math.Max(0, length));
        return !ShellWindowClasses.Contains(name, StringComparer.Ordinal);
    }

    /// <summary>
    /// Covers the monitor's full bounds. A maximized window that still has a caption is a normal maximized app (it may
    /// cover the bounds when the taskbar auto-hides) and does not count.
    /// </summary>
    private static bool CoversMonitor(IntPtr window, PixelRect monitor)
    {
        NativeMethods.Rect rect;
        if (!NativeMethods.GetWindowRect(window, &rect))
        {
            return false;
        }

        var covers = rect.Left <= monitor.Left && rect.Top <= monitor.Top && rect.Right >= monitor.Right && rect.Bottom >= monitor.Bottom;
        if (!covers)
        {
            return false;
        }

        var hasCaption = (NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlStyle) & NativeMethods.WsCaption) == NativeMethods.WsCaption;
        return !(NativeMethods.IsZoomed(window) && hasCaption);
    }

    private static FullscreenSource QueryNotificationState()
    {
        if (NativeMethods.SHQueryUserNotificationState(out var state) != 0)
        {
            return FullscreenSource.None;
        }

        return state switch
        {
            NativeMethods.QunsRunningD3DFullScreen => FullscreenSource.Direct3DFullscreen,
            NativeMethods.QunsPresentationMode => FullscreenSource.PresentationMode,
            _ => FullscreenSource.None,
        };
    }

    /// <summary>Executable file name for exception matching; <c>null</c> when the process cannot be inspected.</summary>
    private static string? ExecutableOf(IntPtr window)
    {
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName + ".exe";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
