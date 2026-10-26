using DynamicIsland.Core.Metrics;

namespace DynamicIsland.Platform.Windows.Metrics;

/// <summary>System readings through cheap Win32 calls (ADR 0021): no performance counters, WMI or elevation.</summary>
public sealed class Win32SystemMetrics : ISystemMetricsService
{
    private const byte BatteryFlagCharging = 8;
    private const byte BatteryFlagNoSystemBattery = 128;
    private const byte BatteryFlagUnknown = 255;
    private const byte PercentUnknown = 255;
    private const byte AcOnline = 1;

    public CpuTimes? GetCpuTimes() =>
        NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user)
            ? new CpuTimes(TimeSpan.FromTicks(idle), TimeSpan.FromTicks(kernel), TimeSpan.FromTicks(user))
            : null;

    public MemoryStatus? GetMemory()
    {
        var status = new NativeMethods.MemoryStatusEx { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MemoryStatusEx>() };
        return NativeMethods.GlobalMemoryStatusEx(ref status) ? new MemoryStatus(status.TotalPhys, status.AvailPhys) : null;
    }

    public BatteryStatus? GetBattery()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var power) || power.BatteryFlag == BatteryFlagUnknown)
        {
            return null;
        }

        if ((power.BatteryFlag & BatteryFlagNoSystemBattery) != 0)
        {
            return new BatteryStatus(HasBattery: false, Percent: null, Charging: false);
        }

        int? percent = power.BatteryLifePercent == PercentUnknown ? null : Math.Clamp((int)power.BatteryLifePercent, 0, 100);
        var charging = (power.BatteryFlag & BatteryFlagCharging) != 0 || power.AcLineStatus == AcOnline;
        return new BatteryStatus(HasBattery: true, percent, charging);
    }

    public TimeSpan GetUptime() => TimeSpan.FromMilliseconds(Environment.TickCount64);
}
