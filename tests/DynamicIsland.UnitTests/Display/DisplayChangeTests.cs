using DynamicIsland.Core.Display;
using DynamicIsland.Core.Settings;
using DynamicIsland.UnitTests.TestSupport;

namespace DynamicIsland.UnitTests.Display;

public sealed class DebouncerTests
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(250);

    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    private int _fired;

    [Fact]
    public void Single_trigger_fires_once_after_the_quiet_period()
    {
        using var debouncer = Create();

        debouncer.Trigger();
        _time.Advance(Quiet - TimeSpan.FromMilliseconds(1));
        Assert.Equal(0, _fired);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, _fired);

        _time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, _fired);
    }

    [Fact]
    public void Burst_of_triggers_is_coalesced_into_one_pass_after_the_last()
    {
        using var debouncer = Create();

        for (var i = 0; i < 5; i++)
        {
            debouncer.Trigger();
            _time.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(0, _fired);
        _time.Advance(Quiet);
        Assert.Equal(1, _fired);
    }

    [Fact]
    public void Separate_bursts_each_fire()
    {
        using var debouncer = Create();

        debouncer.Trigger();
        _time.Advance(Quiet);
        debouncer.Trigger();
        _time.Advance(Quiet);

        Assert.Equal(2, _fired);
    }

    [Fact]
    public void Disposed_debouncer_never_fires()
    {
        var debouncer = Create();
        debouncer.Trigger();

        debouncer.Dispose();
        debouncer.Trigger();
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(0, _fired);
    }

    private Debouncer Create() => new(Quiet, _time, () => _fired++);
}

public sealed class IslandPlacementControllerTests
{
    private static readonly MonitorInfo Primary = new("primary", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 1.0, IsPrimary: true);
    private static readonly MonitorInfo External = new("external", new(1920, 0, 2560, 1440), new(1920, 0, 2560, 1400), 1.5, IsPrimary: false);

    private readonly FakeMonitorService _monitors = new([Primary, External]);

    [Fact]
    public void Island_falls_back_when_the_preferred_monitor_is_unplugged_and_returns_when_it_is_back()
    {
        var controller = CreateController(preferred: "external");

        var first = controller.Update()!;
        Assert.Same(External, first.Result.Monitor);
        Assert.False(first.Result.UsedFallback);

        _monitors.Current = [Primary];
        var unplugged = controller.Update()!;
        Assert.Same(Primary, unplugged.Result.Monitor);
        Assert.True(unplugged.Result.UsedFallback);
        Assert.True(unplugged.Changed);
        Assert.True(unplugged.MonitorChanged);

        _monitors.Current = [Primary, External];
        var replugged = controller.Update()!;
        Assert.Same(External, replugged.Result.Monitor);
        Assert.False(replugged.Result.UsedFallback);
        Assert.True(replugged.MonitorChanged);
    }

    [Fact]
    public void Work_area_change_moves_the_island_without_a_monitor_change()
    {
        var controller = CreateController(preferred: null);
        controller.Update();

        // Taskbar moved to the top: work area now starts at y = 48.
        _monitors.Current = [Primary with { WorkArea = new(0, 48, 1920, 1032) }, External];
        var update = controller.Update()!;

        Assert.True(update.Changed);
        Assert.False(update.MonitorChanged);
        Assert.Equal(48 + 8, update.Result.Rect.Top);
    }

    [Fact]
    public void Repeated_update_without_changes_reports_unchanged()
    {
        var controller = CreateController(preferred: null);
        controller.Update();

        Assert.False(controller.Update()!.Changed);
    }

    [Fact]
    public void No_monitors_keeps_the_last_placement()
    {
        var controller = CreateController(preferred: null);
        var placed = controller.Update()!.Result;

        _monitors.Current = [];

        Assert.Null(controller.Update());
        Assert.Same(placed, controller.Current);
    }

    private IslandPlacementController CreateController(string? preferred) =>
        new(_monitors, () => new PlacementRequest(preferred, IslandPlacement.TopCenter, 0, 8, 200, 36));

    private sealed class FakeMonitorService(IReadOnlyList<MonitorInfo> monitors) : IMonitorService
    {
        public IReadOnlyList<MonitorInfo> Current { get; set; } = monitors;

        public IReadOnlyList<MonitorInfo> GetMonitors() => Current;
    }
}
