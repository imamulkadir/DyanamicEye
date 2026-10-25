using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using DynamicIsland.Core.Display;
using DynamicIsland.Platform.Windows.Display;
using DynamicIsland.TestSupport;

namespace DynamicIsland.IntegrationTests;

/// <summary>Classifies real helper windows on the test host's primary monitor.</summary>
public sealed class Win32FullscreenDetectorTests : IDisposable
{
    private readonly StaDispatcherThread _ui = new();

    public void Dispose() => _ui.Dispose();

    [Fact]
    public void Borderless_window_covering_the_monitor_is_fullscreen_on_that_monitor()
    {
        var primary = PrimaryMonitor();

        var snapshot = WithWindow(primary.Bounds, WindowStyle.None, maximized: false, hwnd => Win32FullscreenDetector.Evaluate(hwnd, ignoreOwnProcess: false));

        Assert.True(snapshot.IsFullscreen);
        Assert.Equal(FullscreenSource.WindowCoversMonitor, snapshot.Source);
        Assert.Equal(primary.Id, snapshot.MonitorId);
        Assert.Equal(Process.GetCurrentProcess().ProcessName + ".exe", snapshot.ProcessExecutable);
    }

    [Fact]
    public void Window_smaller_than_the_monitor_is_not_fullscreen()
    {
        var bounds = PrimaryMonitor().Bounds;
        var half = new PixelRect(bounds.Left, bounds.Top, bounds.Width / 2, bounds.Height / 2);

        var snapshot = WithWindow(half, WindowStyle.None, maximized: false, hwnd => Win32FullscreenDetector.Evaluate(hwnd, ignoreOwnProcess: false));

        AssertNotWindowFullscreen(snapshot);
    }

    [Fact]
    public void Normal_maximized_window_with_a_caption_is_not_fullscreen()
    {
        var snapshot = WithWindow(PrimaryMonitor().WorkArea, WindowStyle.SingleBorderWindow, maximized: true, hwnd => Win32FullscreenDetector.Evaluate(hwnd, ignoreOwnProcess: false));

        AssertNotWindowFullscreen(snapshot);
    }

    [Fact]
    public void Own_process_windows_are_ignored_by_default()
    {
        var snapshot = WithWindow(PrimaryMonitor().Bounds, WindowStyle.None, maximized: false, hwnd => Win32FullscreenDetector.Evaluate(hwnd));

        AssertNotWindowFullscreen(snapshot);
    }

    [Fact]
    public void Desktop_and_missing_windows_are_not_fullscreen_windows()
    {
        AssertNotWindowFullscreen(Win32FullscreenDetector.Evaluate(IntPtr.Zero));
    }

    /// <summary>Windows' own D3D/presentation state may be set on some hosts; it never carries a window's monitor.</summary>
    private static void AssertNotWindowFullscreen(FullscreenSnapshot snapshot)
    {
        Assert.NotEqual(FullscreenSource.WindowCoversMonitor, snapshot.Source);
        Assert.Null(snapshot.MonitorId);
    }

    private static MonitorInfo PrimaryMonitor()
    {
        using var scope = DpiAwarenessScope.PerMonitorV2();
        return new Win32MonitorService().GetMonitors().Single(m => m.IsPrimary);
    }

    private T WithWindow<T>(PixelRect bounds, WindowStyle style, bool maximized, Func<IntPtr, T> evaluate) => _ui.Invoke(() =>
    {
        using var scope = DpiAwarenessScope.PerMonitorV2();
        var window = new Window
        {
            WindowStyle = style,
            ResizeMode = style == WindowStyle.None ? ResizeMode.NoResize : ResizeMode.CanResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Title = "Fullscreen detector test window",
        };
        try
        {
            window.Show();
            var hwnd = new WindowInteropHelper(window).Handle;
            if (maximized)
            {
                window.WindowState = WindowState.Maximized;
            }
            else
            {
                IslandWindowInterop.SetBounds(hwnd, bounds, topmost: false);
            }

            return evaluate(hwnd);
        }
        finally
        {
            window.Close();
        }
    });
}
