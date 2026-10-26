using DynamicIsland.Core.Components;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Widgets;

namespace DynamicIsland.UnitTests.Widgets;

public sealed class WidgetRegistryTests : IDisposable
{
    private static readonly RetryPolicy FastRetries = new() { BaseDelay = TimeSpan.FromMilliseconds(1), MaxDelay = TimeSpan.FromMilliseconds(5) };

    private readonly LifecycleCoordinator _lifecycle = new(TimeProvider.System, TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(2));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _lifecycle.Dispose();

    [Fact]
    public async Task Enabled_widgets_run_and_appear_in_the_users_order()
    {
        var clock = new FakeWidget(WidgetIds.Clock);
        var media = new FakeWidget(WidgetIds.Media);
        using var host = CreateHost();
        var registry = new WidgetRegistry(host, [clock, media]);

        registry.Register(Settings((WidgetIds.Media, true), (WidgetIds.Clock, true), (WidgetIds.Notes, true)));
        host.StartAll();
        await WaitUntilAsync(() => clock.IsRunning && media.IsRunning && registry.Slots.All(s => s.Status.State == ComponentState.Healthy));

        Assert.Equal([WidgetIds.Media, WidgetIds.Clock], registry.Slots.Select(s => s.Id)); // Notes has no module yet.
        Assert.Equal("Media", registry.Slots[0].Label);
    }

    [Fact]
    public async Task Disabled_widget_never_starts_and_is_not_shown()
    {
        var clock = new FakeWidget(WidgetIds.Clock);
        using var host = CreateHost();
        var registry = new WidgetRegistry(host, [clock]);

        registry.Register(Settings((WidgetIds.Clock, false)));
        host.StartAll();
        await Task.Delay(100, Ct);

        Assert.Equal(0, clock.Runs);
        Assert.Empty(registry.Slots);
        Assert.Equal("disabled", host.StatusOf(clock.Name)!.ReasonCode);
    }

    [Fact]
    public async Task Disabling_stops_background_work_and_releases_content_and_enabling_restarts_it()
    {
        var clock = new FakeWidget(WidgetIds.Clock);
        using var host = CreateHost();
        var registry = new WidgetRegistry(host, [clock]);
        registry.Register(Settings((WidgetIds.Clock, true)));
        host.StartAll();
        await WaitUntilAsync(() => clock.Snapshot is not null);

        await registry.ApplyAsync(Settings((WidgetIds.Clock, false)), Ct);

        Assert.False(clock.IsRunning);
        Assert.True(clock.WasCancelled);
        Assert.Null(clock.Snapshot);
        Assert.Equal(1, clock.Releases);
        Assert.Empty(registry.Slots);
        Assert.Equal(ComponentState.Stopped, host.StatusOf(clock.Name)!.State);

        await registry.ApplyAsync(Settings((WidgetIds.Clock, true)), Ct);
        await WaitUntilAsync(() => clock.Runs == 2 && clock.Snapshot is not null);
        Assert.Single(registry.Slots);
    }

    [Fact]
    public async Task Reordering_changes_slots_without_restarting_widgets()
    {
        var clock = new FakeWidget(WidgetIds.Clock);
        var media = new FakeWidget(WidgetIds.Media);
        using var host = CreateHost();
        var registry = new WidgetRegistry(host, [clock, media]);
        registry.Register(Settings((WidgetIds.Clock, true), (WidgetIds.Media, true)));
        host.StartAll();
        await WaitUntilAsync(() => clock.IsRunning && media.IsRunning);
        var changes = 0;
        registry.Changed += (_, _) => Interlocked.Increment(ref changes);

        await registry.ApplyAsync(Settings((WidgetIds.Media, true), (WidgetIds.Clock, true)), Ct);

        Assert.Equal([WidgetIds.Media, WidgetIds.Clock], registry.Slots.Select(s => s.Id));
        Assert.Equal(1, clock.Runs);
        Assert.Equal(1, media.Runs);
        Assert.True(changes > 0);
    }

    [Fact]
    public async Task A_failing_widget_is_shown_as_unavailable_while_others_keep_running()
    {
        var broken = new FakeWidget(WidgetIds.Media) { Failure = new InvalidOperationException("bug") };
        var clock = new FakeWidget(WidgetIds.Clock);
        using var host = CreateHost();
        var registry = new WidgetRegistry(host, [broken, clock], FastRetries);
        registry.Register(Settings((WidgetIds.Media, true), (WidgetIds.Clock, true)));

        host.StartAll();
        await WaitUntilAsync(() => registry.Slots[0].IsUnavailable);

        Assert.True(clock.IsRunning);
        Assert.False(registry.Slots[1].IsUnavailable);
        Assert.Null(_lifecycle.Reason);
    }

    [Fact]
    public async Task Pause_stops_widgets_resume_restarts_only_enabled_ones()
    {
        var clock = new FakeWidget(WidgetIds.Clock);
        var media = new FakeWidget(WidgetIds.Media);
        using var host = CreateHost();
        var registry = new WidgetRegistry(host, [clock, media]);
        registry.Register(Settings((WidgetIds.Clock, true), (WidgetIds.Media, false)));
        host.StartAll();
        await WaitUntilAsync(() => clock.IsRunning);

        await host.SetPausedAsync(true, Ct);
        Assert.False(clock.IsRunning);

        // Enabling while paused does not start it until resume.
        await registry.ApplyAsync(Settings((WidgetIds.Clock, true), (WidgetIds.Media, true)), Ct);
        Assert.Equal(0, media.Runs);

        await host.SetPausedAsync(false, Ct);
        await WaitUntilAsync(() => clock.IsRunning && media.IsRunning);
    }

    [Fact]
    public async Task Safe_mode_skips_widgets_and_enabling_does_not_start_them()
    {
        var clock = new FakeWidget(WidgetIds.Clock);
        using var host = CreateHost(new LaunchArguments(SafeMode: true, IgnoredCount: 0));
        var registry = new WidgetRegistry(host, [clock]);
        registry.Register(Settings((WidgetIds.Clock, false)));
        host.StartAll();

        await registry.ApplyAsync(Settings((WidgetIds.Clock, true)), Ct);
        await Task.Delay(100, Ct);

        Assert.Equal(0, clock.Runs);
        Assert.Equal(ComponentState.SkippedSafeMode, registry.Slots.Single().Status.State);
    }

    [Fact]
    public void Widgets_must_use_known_ids_matching_names_and_be_unique()
    {
        using var host = CreateHost();

        Assert.Throws<ArgumentException>(() => new WidgetRegistry(host, [new FakeWidget("weather")]));
        Assert.Throws<ArgumentException>(() => new WidgetRegistry(host, [new FakeWidget(WidgetIds.Clock), new FakeWidget(WidgetIds.Clock)]));
        Assert.Throws<ArgumentException>(() => new WidgetRegistry(host, [new FakeWidget(WidgetIds.Clock, name: "clock")]));
    }

    private ComponentHost CreateHost(LaunchArguments? launch = null) =>
        new(_lifecycle, new InMemoryNotificationService(TimeProvider.System), new DefaultFailureClassifier(), launch ?? LaunchArguments.Default, TimeProvider.System);

    private static WidgetSetting[] Settings(params (string Id, bool Enabled)[] widgets) => [.. widgets.Select(w => new WidgetSetting(w.Id, w.Enabled))];

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition.");
            await Task.Delay(10, Ct);
        }
    }

    private sealed class FakeWidget(string id, string? name = null) : IWidget
    {
        private object? _snapshot;
        private int _running;

        public event EventHandler? SnapshotChanged;

        public string Id { get; } = id;

        public string Name { get; } = name ?? WidgetRegistry.ComponentName(id);

        public object? Snapshot => Volatile.Read(ref _snapshot);

        public Exception? Failure { get; init; }

        public int Runs { get; private set; }

        public int Releases { get; private set; }

        public bool IsRunning => Volatile.Read(ref _running) == 1;

        public bool WasCancelled { get; private set; }

        public async Task RunAsync(IComponentContext context, CancellationToken cancellationToken)
        {
            Runs++;
            if (Failure is not null)
            {
                throw Failure;
            }

            Volatile.Write(ref _running, 1);
            try
            {
                Volatile.Write(ref _snapshot, $"content {Runs}");
                SnapshotChanged?.Invoke(this, EventArgs.Empty);
                context.ReportHealthy();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }
            finally
            {
                Volatile.Write(ref _running, 0);
            }
        }

        public void Release()
        {
            Releases++;
            Volatile.Write(ref _snapshot, null);
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
