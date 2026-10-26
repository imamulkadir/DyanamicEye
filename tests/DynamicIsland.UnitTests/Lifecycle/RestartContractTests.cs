using DynamicIsland.Core.Components;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Notifications;
using DynamicIsland.UnitTests.TestSupport;

namespace DynamicIsland.UnitTests.Lifecycle;

public sealed class LauncherArgumentsTests
{
    [Fact]
    public void No_arguments_is_a_normal_supervised_start()
    {
        Assert.Equal(new LauncherArguments(false, null, 0), LauncherArguments.Parse([]));
    }

    [Fact]
    public void Safe_mode_and_previous_instance_are_recognized()
    {
        var parsed = LauncherArguments.Parse(["--after-pid", "4242", "--SAFE-MODE"]);

        Assert.True(parsed.SafeMode);
        Assert.Equal(4242, parsed.AfterProcessId);
        Assert.Equal(0, parsed.IgnoredCount);
    }

    [Theory]
    [InlineData("--after-pid", "-5")]
    [InlineData("--after-pid", "0")]
    [InlineData("--after-pid", "abc")]
    [InlineData("--after-pid", "1e3")]
    public void Invalid_process_ids_are_ignored(string flag, string value)
    {
        var parsed = LauncherArguments.Parse([flag, value]);

        Assert.Null(parsed.AfterProcessId);
        Assert.Equal(2, parsed.IgnoredCount);
    }

    [Fact]
    public void Missing_process_id_value_is_ignored()
    {
        var parsed = LauncherArguments.Parse(["--after-pid"]);

        Assert.Null(parsed.AfterProcessId);
        Assert.Equal(1, parsed.IgnoredCount);
    }
}

public sealed class SafeModeRestartTests
{
    [Theory]
    [InlineData(ExitReason.Restart, ExitCodes.RestartRequested)]
    [InlineData(ExitReason.RestartInSafeMode, ExitCodes.RestartInSafeModeRequested)]
    public void Restart_reasons_map_to_launcher_exit_codes(ExitReason reason, int code)
    {
        Assert.Equal(code, ExitCodes.For(reason));
    }

    [Fact]
    public async Task Safe_mode_restart_relaunches_in_safe_mode_without_counting_a_crash()
    {
        var runner = new Runner(ExitCodes.RestartInSafeModeRequested, ExitCodes.RestartRequested, ExitCodes.Success);
        var store = new NoCrashStore();
        var supervisor = new AppSupervisor(runner, store, new Notifier(), new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var result = await supervisor.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, result);
        Assert.Equal([false, true, false], runner.Runs);
        Assert.Equal(0, store.Crashes);
    }

    [Fact]
    public async Task Launcher_can_start_directly_in_safe_mode()
    {
        var runner = new Runner(ExitCodes.Success);
        var supervisor = new AppSupervisor(runner, new NoCrashStore(), new Notifier(), new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        await supervisor.RunAsync(null, TestContext.Current.CancellationToken, startInSafeMode: true);

        Assert.Equal([true], runner.Runs);
    }

    private sealed class Runner(params int[] codes) : IAppProcessRunner
    {
        private int _next;

        public List<bool> Runs { get; } = [];

        public Task<int> RunAsync(bool safeMode, CancellationToken cancellationToken)
        {
            Runs.Add(safeMode);
            return Task.FromResult(codes[_next++]);
        }
    }

    private sealed class NoCrashStore : ICrashStateStore
    {
        public int Crashes { get; private set; }

        public IReadOnlyList<DateTimeOffset> RecordCrash(DateTimeOffset now)
        {
            Crashes++;
            return [now];
        }
    }

    private sealed class Notifier : IRecoveryNotifier
    {
        public void NotifyStopped()
        {
        }
    }
}

public sealed class ComponentPauseTests : IDisposable
{
    private readonly LifecycleCoordinator _lifecycle = new(TimeProvider.System);

    public void Dispose() => _lifecycle.Dispose();

    [Fact]
    public async Task Pause_stops_optional_components_and_resume_restarts_them()
    {
        using var host = new ComponentHost(_lifecycle, new InMemoryNotificationService(TimeProvider.System), new DefaultFailureClassifier(), LaunchArguments.Default, TimeProvider.System);
        var widget = new CountingComponent("widget");
        var core = new CountingComponent("core");
        host.Add(widget, optional: true);
        host.Add(core, optional: false);
        host.StartAll();
        await WaitAsync(() => widget.Starts == 1 && core.Starts == 1);

        await host.SetPausedAsync(true, TestContext.Current.CancellationToken);
        var paused = host.Statuses.Single(s => s.Name == "widget");

        await host.SetPausedAsync(false, TestContext.Current.CancellationToken);
        await WaitAsync(() => widget.Starts == 2);

        Assert.Equal(ComponentState.Stopped, paused.State);
        Assert.Equal("paused", paused.ReasonCode);
        Assert.Equal(1, core.Starts);
        Assert.Equal(ComponentState.Healthy, host.Statuses.Single(s => s.Name == "widget").State);
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition not reached in 5 s.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class CountingComponent(string name) : IIsolatedComponent
    {
        private int _starts;

        public string Name => name;

        public int Starts => Volatile.Read(ref _starts);

        public async Task RunAsync(IComponentContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _starts);
            context.ReportHealthy();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}
