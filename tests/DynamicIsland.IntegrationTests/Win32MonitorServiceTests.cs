using DynamicIsland.Platform.Windows.Display;

namespace DynamicIsland.IntegrationTests;

/// <summary>Runs against the real displays of the test host (dev box or CI runner).</summary>
public sealed class Win32MonitorServiceTests
{
    [Fact]
    public void Host_reports_consistent_monitors_with_exactly_one_primary()
    {
        var monitors = new Win32MonitorService().GetMonitors();

        Assert.NotEmpty(monitors);
        Assert.Single(monitors, m => m.IsPrimary);
        Assert.Equal(monitors.Count, monitors.Select(m => m.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(monitors, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Id));
            Assert.True(m.Bounds.Width > 0 && m.Bounds.Height > 0);
            Assert.True(m.Bounds.Contains(m.WorkArea), "Work area must lie inside the monitor bounds.");
            Assert.InRange(m.DpiScale, 1.0, 5.0);
        });
    }

    [Fact]
    public void Monitor_ids_are_stable_between_calls()
    {
        var service = new Win32MonitorService();

        Assert.Equal(service.GetMonitors().Select(m => m.Id), service.GetMonitors().Select(m => m.Id));
    }
}
