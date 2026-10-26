using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DynamicIsland.App;
using DynamicIsland.Core.Components;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Metrics;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Widgets;
using DynamicIsland.Platform.Windows.Metrics;
using DynamicIsland.TestSupport;
using DynamicIsland.Widgets;

namespace DynamicIsland.IntegrationTests;

/// <summary>System metrics (P04-04, ADR 0021): real Win32 readings and the tiles view.</summary>
public sealed class MetricsIntegrationTests : IDisposable
{
    private readonly StaDispatcherThread _ui = new();

    public void Dispose() => _ui.Dispose();

    [Fact]
    public async Task Win32_readings_are_plausible()
    {
        var metrics = new Win32SystemMetrics();

        var first = metrics.GetCpuTimes();
        await Task.Delay(300, TestContext.Current.CancellationToken);
        var second = metrics.GetCpuTimes();
        var memory = metrics.GetMemory();
        var battery = metrics.GetBattery();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.InRange(CpuUsage.Between(first!.Value, second!.Value) ?? 0, 0, 100);
        Assert.True(memory!.Value.TotalBytes > 1UL << 30, "At least 1 GB of memory.");
        Assert.InRange(memory.Value.UsedPercent, 0.1, 100);
        Assert.True(metrics.GetUptime() > TimeSpan.Zero);
        Assert.True(battery is null || battery.Value.HasBattery || battery.Value.Percent is null);
        Assert.True(battery?.Percent is null or (>= 0 and <= 100));
    }

    [Theory]
    [InlineData(true, new[] { "Processor: 42%", "Memory used: 9.0 / 16 GB", "Battery, charging: 76% ⚡", "Time since Windows started: 2 d 3 h" })]
    [InlineData(false, new[] { "Processor: 42%", "Memory used: 9.0 / 16 GB", "Time since Windows started: 2 d 3 h" })]
    public void Tiles_show_each_value_and_hide_battery_without_one(bool hasBattery, string[] names)
    {
        var snapshot = new MetricsSnapshot(42, new MemoryStatus(16UL << 30, 7UL << 30), new BatteryStatus(hasBattery, hasBattery ? 76 : null, hasBattery), TimeSpan.FromHours(51));

        var spoken = _ui.Invoke(() =>
        {
            var culture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            try
            {
                using var lifecycle = new LifecycleCoordinator(TimeProvider.System);
                using var host = new ComponentHost(lifecycle, new InMemoryNotificationService(TimeProvider.System), new DefaultFailureClassifier(), LaunchArguments.Default, TimeProvider.System);
                using var registry = new WidgetRegistry(host, []);
                var view = WidgetPresenter.Views[WidgetIds.SystemStats](
                    new WidgetSlot(WidgetIds.SystemStats, "System stats", new ComponentStatus("widget-system", ComponentState.Healthy), snapshot),
                    null!)!;
                return LogicalTreeHelper.GetChildren(view).OfType<StackPanel>().Select(AutomationProperties.GetName).ToArray();
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }
        });

        Assert.Equal(names, spoken);
    }
}
