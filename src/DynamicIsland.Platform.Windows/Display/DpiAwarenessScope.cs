namespace DynamicIsland.Platform.Windows.Display;

/// <summary>
/// Makes the current thread per-monitor-v2 DPI aware until disposed, so Win32 coordinates are physical pixels.
/// The app process is already aware through its manifest; tools and tests that measure the app need this.
/// </summary>
public sealed class DpiAwarenessScope : IDisposable
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    private readonly IntPtr _previous;

    private DpiAwarenessScope(IntPtr previous) => _previous = previous;

    public static DpiAwarenessScope PerMonitorV2() => new(NativeMethods.SetThreadDpiAwarenessContext(PerMonitorAwareV2));

    public void Dispose()
    {
        if (_previous != IntPtr.Zero)
        {
            NativeMethods.SetThreadDpiAwarenessContext(_previous);
        }
    }
}
