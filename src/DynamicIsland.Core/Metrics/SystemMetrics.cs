using System.Globalization;

namespace DynamicIsland.Core.Metrics;

/// <summary>Cumulative CPU times since boot, summed over all processors (<c>GetSystemTimes</c>; kernel includes idle).</summary>
public readonly record struct CpuTimes(TimeSpan Idle, TimeSpan Kernel, TimeSpan User);

public readonly record struct MemoryStatus(ulong TotalBytes, ulong AvailableBytes)
{
    public ulong UsedBytes => TotalBytes >= AvailableBytes ? TotalBytes - AvailableBytes : 0;

    public double UsedPercent => TotalBytes == 0 ? 0 : 100.0 * UsedBytes / TotalBytes;
}

/// <param name="Percent">0–100, or <c>null</c> when Windows does not know.</param>
public readonly record struct BatteryStatus(bool HasBattery, int? Percent, bool Charging);

/// <summary>Guide §3 contract: cheap, standard-user system readings (ADR 0021). A reading that fails returns <c>null</c>.</summary>
public interface ISystemMetricsService
{
    CpuTimes? GetCpuTimes();

    MemoryStatus? GetMemory();

    BatteryStatus? GetBattery();

    TimeSpan GetUptime();
}

/// <summary>CPU usage between two cumulative samples.</summary>
public static class CpuUsage
{
    /// <returns>Busy share 0–100, or <c>null</c> for a zero interval or counters that went backwards (reset).</returns>
    public static double? Between(CpuTimes earlier, CpuTimes later)
    {
        var idle = later.Idle - earlier.Idle;
        var total = (later.Kernel - earlier.Kernel) + (later.User - earlier.User);
        if (total <= TimeSpan.Zero || idle < TimeSpan.Zero || idle > total)
        {
            return null;
        }

        return Math.Clamp(100.0 * (total - idle).Ticks / total.Ticks, 0, 100);
    }
}

/// <summary>Short texts for the metrics tiles.</summary>
public static class MetricsFormat
{
    public static string Percent(double? value, CultureInfo culture) =>
        value is { } v ? Math.Round(v).ToString("0", culture) + "%" : "—";

    /// <summary>"8.1 / 16 GB".</summary>
    public static string Memory(MemoryStatus? memory, CultureInfo culture)
    {
        if (memory is not { TotalBytes: > 0 } m)
        {
            return "—";
        }

        const double Gb = 1024.0 * 1024 * 1024;
        return $"{(m.UsedBytes / Gb).ToString("0.0", culture)} / {Math.Round(m.TotalBytes / Gb).ToString("0", culture)} GB";
    }

    /// <summary>"2 d 3 h", "4 h 12 m", "35 m".</summary>
    public static string Uptime(TimeSpan uptime)
    {
        if (uptime < TimeSpan.Zero)
        {
            uptime = TimeSpan.Zero;
        }

        return uptime.TotalDays >= 1 ? $"{(int)uptime.TotalDays} d {uptime.Hours} h"
            : uptime.TotalHours >= 1 ? $"{(int)uptime.TotalHours} h {uptime.Minutes} m"
            : $"{(int)uptime.TotalMinutes} m";
    }
}
