using System.Globalization;
using DynamicIsland.Core.Components;
using DynamicIsland.Core.Metrics;
using DynamicIsland.Widgets;

namespace DynamicIsland.UnitTests.Widgets;

public sealed class MetricsMathTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void Cpu_usage_is_busy_time_over_total_time_where_kernel_includes_idle()
    {
        var earlier = new CpuTimes(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(50));
        var later = new CpuTimes(TimeSpan.FromSeconds(106), TimeSpan.FromSeconds(158), TimeSpan.FromSeconds(52)); // 10 s total, 6 s idle.

        Assert.Equal(40, CpuUsage.Between(earlier, later)!.Value, precision: 6);
    }

    [Fact]
    public void Zero_interval_and_counter_resets_give_no_value()
    {
        var sample = new CpuTimes(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));

        Assert.Null(CpuUsage.Between(sample, sample));
        Assert.Null(CpuUsage.Between(sample, new CpuTimes(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData(35.6, "36%")]
    [InlineData(null, "—")]
    public void Percent_text(double? value, string expected) => Assert.Equal(expected, MetricsFormat.Percent(value, English));

    [Fact]
    public void Memory_text_shows_used_of_total()
    {
        Assert.Equal("9.0 / 16 GB", MetricsFormat.Memory(new MemoryStatus(16UL << 30, 7UL << 30), English));
        Assert.Equal("—", MetricsFormat.Memory(null, English));
        Assert.Equal(0UL, new MemoryStatus(1, 2).UsedBytes);
    }

    [Theory]
    [InlineData(35, "35 m")]
    [InlineData(4 * 60 + 12, "4 h 12 m")]
    [InlineData(51 * 60, "2 d 3 h")]
    [InlineData(-5, "0 m")]
    public void Uptime_text(int minutes, string expected) => Assert.Equal(expected, MetricsFormat.Uptime(TimeSpan.FromMinutes(minutes)));
}

/// <summary>The metrics widget's cadence (P04-04, ADR 0021) on real time with scripted readings; waits are bounded.</summary>
public sealed class MetricsWidgetTests : IDisposable
{
    private readonly FakeMetrics _metrics = new();
    private readonly MetricsWidget _widget;
    private readonly CancellationTokenSource _stop = new();

    public MetricsWidgetTests() => _widget = new MetricsWidget(_metrics, TimeProvider.System);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _widget.Dispose();
    }

    [Fact]
    public async Task Collapsed_island_takes_no_samples()
    {
        _ = Start(); // Ends with cancellation in Dispose.

        await Task.Delay(700, Ct);

        Assert.Equal(0, _widget.Samples);

        // Only a placeholder, so the widget gets a tab (ADR 0025): no readings, up time from the tick count.
        Assert.Equal(new MetricsSnapshot(null, null, null, TimeSpan.FromHours(3)), _widget.Snapshot);
    }

    [Fact]
    public async Task Expanding_shows_values_at_once_and_cpu_after_the_baseline()
    {
        _ = Start(); // Ends with cancellation in Dispose.

        _widget.SetExpanded(true);
        await WaitUntilAsync(() => _widget.Samples >= 1);
        var first = (MetricsSnapshot)_widget.Snapshot!;
        Assert.Null(first.CpuPercent);
        Assert.Equal(_metrics.Memory, first.Memory);
        Assert.Equal(TimeSpan.FromHours(3), first.Uptime);

        await WaitUntilAsync(() => ((MetricsSnapshot)_widget.Snapshot!).CpuPercent is not null);
        Assert.Equal(50, ((MetricsSnapshot)_widget.Snapshot!).CpuPercent!.Value, precision: 6);
        Assert.Equal(2, _widget.Samples);
    }

    [Fact]
    public async Task Samples_every_two_seconds_while_expanded_and_stop_when_collapsed_or_hidden()
    {
        _ = Start(); // Ends with cancellation in Dispose.
        _widget.SetExpanded(true);
        await WaitUntilAsync(() => _widget.Samples >= 2);

        await Task.Delay(2300, Ct);
        Assert.InRange(_widget.Samples, 3, 4);

        _widget.SetExpanded(false);
        await Task.Delay(200, Ct);
        var collapsed = _widget.Samples;
        await Task.Delay(2300, Ct);
        Assert.Equal(collapsed, _widget.Samples);

        _widget.SetActive(false); // Expanded but hidden (e.g. a fullscreen app) still samples nothing.
        _widget.SetExpanded(true);
        await Task.Delay(700, Ct);
        Assert.Equal(collapsed, _widget.Samples);
    }

    [Fact]
    public async Task Resume_starts_a_new_cpu_baseline_and_release_drops_the_values()
    {
        _ = Start(); // Ends with cancellation in Dispose.
        _widget.SetExpanded(true);
        await WaitUntilAsync(() => ((MetricsSnapshot?)_widget.Snapshot)?.CpuPercent is not null);

        _metrics.ResetCounters(); // Counters went backwards (sleep or reset).
        _widget.OnSystemChanged();
        await WaitUntilAsync(() => ((MetricsSnapshot?)_widget.Snapshot)?.CpuPercent is null);

        _widget.Release();
        Assert.Null(_widget.Snapshot);
    }

    [Fact]
    public async Task Failed_readings_leave_values_out_without_failing_the_widget()
    {
        _metrics.Fail = true;
        var run = Start();
        _widget.SetExpanded(true);

        await WaitUntilAsync(() => _widget.Samples >= 2);
        var snapshot = (MetricsSnapshot)_widget.Snapshot!;

        Assert.Null(snapshot.CpuPercent);
        Assert.Null(snapshot.Memory);
        Assert.Null(snapshot.Battery);
        Assert.False(run.IsCompleted);
    }

    private Task Start() => Task.Run(() => _widget.RunAsync(new Context(), _stop.Token), Ct);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition.");
            await Task.Delay(10, Ct);
        }
    }

    private sealed class Context : IComponentContext
    {
        public void ReportHealthy()
        {
        }

        public void ReportDegraded(string reasonCode)
        {
        }
    }

    /// <summary>Each CPU reading adds 1 s busy and 1 s idle: 50 % between any two readings.</summary>
    private sealed class FakeMetrics : ISystemMetricsService
    {
        private long _seconds = 100;

        public MemoryStatus Memory { get; } = new(8UL << 30, 2UL << 30);

        public bool Fail { get; set; }

        public void ResetCounters() => Interlocked.Exchange(ref _seconds, 0);

        public CpuTimes? GetCpuTimes()
        {
            if (Fail)
            {
                return null;
            }

            var s = Interlocked.Add(ref _seconds, 1);
            return new CpuTimes(TimeSpan.FromSeconds(s), TimeSpan.FromSeconds(2 * s), TimeSpan.Zero);
        }

        public MemoryStatus? GetMemory() => Fail ? null : Memory;

        public BatteryStatus? GetBattery() => Fail ? null : new BatteryStatus(false, null, false);

        public TimeSpan GetUptime() => TimeSpan.FromHours(3);
    }
}
