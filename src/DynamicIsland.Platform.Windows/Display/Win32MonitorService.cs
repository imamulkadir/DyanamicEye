using System.Runtime.InteropServices;
using DynamicIsland.Core.Display;

namespace DynamicIsland.Platform.Windows.Display;

/// <summary>
/// Monitors via <c>EnumDisplayMonitors</c>/<c>GetMonitorInfo</c>/<c>GetDpiForMonitor</c> (physical pixels; requires a
/// per-monitor-DPI-aware process). The id is the monitor's device interface path, which survives reboots and
/// re-plugging into the same port better than the volatile <c>\\.\DISPLAYn</c> name used as a fallback.
/// </summary>
public sealed unsafe class Win32MonitorService : IMonitorService
{
    private const double DefaultDpi = 96.0;

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var handles = new List<IntPtr>();
        var handle = GCHandle.Alloc(handles);
        try
        {
            if (!NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, &CollectMonitor, GCHandle.ToIntPtr(handle)))
            {
                throw new InvalidOperationException("EnumDisplayMonitors failed.");
            }
        }
        finally
        {
            handle.Free();
        }

        return handles.Select(Describe).OfType<MonitorInfo>().ToArray();
    }

    [UnmanagedCallersOnly]
    private static int CollectMonitor(IntPtr monitor, IntPtr hdc, NativeMethods.Rect* clip, IntPtr data)
    {
        var handles = (List<IntPtr>)GCHandle.FromIntPtr(data).Target!;
        handles.Add(monitor);
        return 1;
    }

    private static MonitorInfo? Describe(IntPtr monitor)
    {
        var info = new NativeMethods.MonitorInfoEx { Size = (uint)sizeof(NativeMethods.MonitorInfoEx) };
        if (!NativeMethods.GetMonitorInfo(monitor, &info))
        {
            // Monitor disappeared between enumeration and query.
            return null;
        }

        var deviceName = new string(info.Device);
        var dpi = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MonitorDpiEffective, out var dpiX, out _) == 0 ? dpiX : DefaultDpi;

        return new MonitorInfo(
            StableId(deviceName),
            ToPixelRect(info.Monitor),
            ToPixelRect(info.Work),
            dpi / DefaultDpi,
            (info.Flags & NativeMethods.MonitorInfoPrimary) != 0);
    }

    /// <summary>Id of a monitor handle using the same scheme as <see cref="GetMonitors"/>, or <c>null</c>.</summary>
    internal static string? IdFor(IntPtr monitor)
    {
        var info = new NativeMethods.MonitorInfoEx { Size = (uint)sizeof(NativeMethods.MonitorInfoEx) };
        return NativeMethods.GetMonitorInfo(monitor, &info) ? StableId(new string(info.Device)) : null;
    }

    /// <summary>Full bounds of a monitor handle in physical pixels.</summary>
    internal static PixelRect? BoundsOf(IntPtr monitor)
    {
        var info = new NativeMethods.MonitorInfoEx { Size = (uint)sizeof(NativeMethods.MonitorInfoEx) };
        return NativeMethods.GetMonitorInfo(monitor, &info) ? ToPixelRect(info.Monitor) : null;
    }

    private static string StableId(string adapterDeviceName)
    {
        var device = new NativeMethods.DisplayDevice { Size = (uint)sizeof(NativeMethods.DisplayDevice) };
        if (NativeMethods.EnumDisplayDevices(adapterDeviceName, 0, &device, NativeMethods.EddGetDeviceInterfaceName))
        {
            var interfacePath = new string(device.DeviceId);
            if (!string.IsNullOrWhiteSpace(interfacePath))
            {
                return interfacePath;
            }
        }

        return adapterDeviceName;
    }

    private static PixelRect ToPixelRect(NativeMethods.Rect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
}
