using DynamicIsland.Core.Settings;

namespace DynamicIsland.Core.Display;

/// <summary>Rectangle in physical screen pixels (virtual-screen coordinates; may be negative).</summary>
public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public bool Contains(PixelRect other) =>
        other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;
}

/// <param name="Id">Stable identifier persisted in settings (device interface path where available). Never logged.</param>
/// <param name="Bounds">Full monitor area in physical pixels.</param>
/// <param name="WorkArea">Area excluding taskbar and app bars, in physical pixels.</param>
/// <param name="DpiScale">Effective DPI / 96 (1.0 = 100 %).</param>
public sealed record MonitorInfo(string Id, PixelRect Bounds, PixelRect WorkArea, double DpiScale, bool IsPrimary);

/// <summary>Enumerates attached monitors (guide §3 contract).</summary>
public interface IMonitorService
{
    IReadOnlyList<MonitorInfo> GetMonitors();
}

/// <param name="WidthDip">Island width in device-independent pixels (96 DPI units), before monitor scaling.</param>
public sealed record PlacementRequest(
    string? PreferredMonitorId,
    IslandPlacement Placement,
    double HorizontalOffsetDip,
    double VerticalOffsetDip,
    double WidthDip,
    double HeightDip);

/// <param name="UsedFallback">The preferred monitor is not attached; the stored preference must be kept for later.</param>
public sealed record PlacementResult(MonitorInfo Monitor, PixelRect Rect, bool UsedFallback);

/// <summary>
/// Pure placement (D05, D08): chooses the preferred monitor or falls back to primary, then the first monitor; positions the
/// island inside the monitor's work area (never under the taskbar) and keeps it fully visible.
/// Horizontal offset moves right for top-left/center and inward (left) for top-right.
/// </summary>
public static class IslandPlacementCalculator
{
    public static PlacementResult Place(IReadOnlyList<MonitorInfo> monitors, PlacementRequest request)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(request);
        if (monitors.Count == 0)
        {
            throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        }

        var preferred = request.PreferredMonitorId is null
            ? null
            : monitors.FirstOrDefault(m => string.Equals(m.Id, request.PreferredMonitorId, StringComparison.OrdinalIgnoreCase));
        var monitor = preferred ?? monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        var usedFallback = request.PreferredMonitorId is not null && preferred is null;

        var area = monitor.WorkArea;
        var scale = monitor.DpiScale > 0 ? monitor.DpiScale : 1.0;
        var width = Math.Clamp(ToPixels(request.WidthDip, scale), 1, area.Width);
        var height = Math.Clamp(ToPixels(request.HeightDip, scale), 1, area.Height);
        var horizontalOffset = ToPixels(request.HorizontalOffsetDip, scale);

        var left = request.Placement switch
        {
            IslandPlacement.TopLeft => area.Left + horizontalOffset,
            IslandPlacement.TopRight => area.Right - width - horizontalOffset,
            _ => area.Left + ((area.Width - width) / 2) + horizontalOffset,
        };
        var top = area.Top + ToPixels(request.VerticalOffsetDip, scale);

        left = Math.Clamp(left, area.Left, area.Right - width);
        top = Math.Clamp(top, area.Top, area.Bottom - height);

        return new PlacementResult(monitor, new PixelRect(left, top, width, height), usedFallback);
    }

    private static int ToPixels(double dips, double scale) =>
        double.IsFinite(dips) ? (int)Math.Round(dips * scale, MidpointRounding.AwayFromZero) : 0;
}
