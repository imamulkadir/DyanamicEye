using DynamicIsland.Core.Lifecycle;

namespace DynamicIsland.UnitTests.Lifecycle;

public sealed class LifecycleCoordinatorTests
{
    private static readonly TimeSpan ShortStep = TimeSpan.FromMilliseconds(150);

    [Fact]
    public void First_shutdown_request_wins_and_cancels_the_app_token()
    {
        using var coordinator = new LifecycleCoordinator(TimeProvider.System);
        var raised = new List<ShutdownRequestedEventArgs>();
        coordinator.ShutdownRequested += (_, e) => raised.Add(e);

        Assert.True(coordinator.RequestShutdown(ExitReason.SessionEnding));
        Assert.False(coordinator.RequestShutdown(ExitReason.Fatal));

        Assert.True(coordinator.ApplicationStopping.IsCancellationRequested);
        Assert.Equal(ExitReason.SessionEnding, coordinator.Reason);
        Assert.Equal(ExitCodes.Success, coordinator.ExitCode);
        var single = Assert.Single(raised);
        Assert.Equal(ExitReason.SessionEnding, single.Reason);
    }

    [Theory]
    [InlineData(ExitReason.UserExit, ExitCodes.Success)]
    [InlineData(ExitReason.SessionEnding, ExitCodes.Success)]
    [InlineData(ExitReason.Restart, ExitCodes.RestartRequested)]
    [InlineData(ExitReason.Fatal, ExitCodes.Fatal)]
    public void Exit_code_follows_the_reason(ExitReason reason, int expected)
    {
        using var coordinator = new LifecycleCoordinator(TimeProvider.System);

        coordinator.RequestShutdown(reason);

        Assert.Equal(expected, coordinator.ExitCode);
    }

    [Fact]
    public void Explicit_exit_code_overrides_the_default()
    {
        using var coordinator = new LifecycleCoordinator(TimeProvider.System);

        coordinator.RequestShutdown(ExitReason.Fatal, 42);

        Assert.Equal(42, coordinator.ExitCode);
    }

    [Fact]
    public async Task Components_stop_in_reverse_registration_order()
    {
        using var coordinator = new LifecycleCoordinator(TimeProvider.System);
        var order = new List<string>();
        foreach (var name in new[] { "settings", "widgets", "shell" })
        {
            coordinator.Register(name, _ =>
            {
                order.Add(name);
                return Task.CompletedTask;
            });
        }

        var report = await coordinator.ShutdownAsync();

        Assert.Equal(["shell", "widgets", "settings"], order);
        Assert.All(report.Steps, s => Assert.Equal(ShutdownStepStatus.Completed, s.Status));
        Assert.Equal(ExitReason.UserExit, report.Reason);
    }

    [Fact]
    public async Task Hanging_component_times_out_and_the_rest_still_stop()
    {
        using var coordinator = new LifecycleCoordinator(TimeProvider.System, ShortStep, TimeSpan.FromSeconds(5));
        var lastStopped = false;
        coordinator.Register("last", _ =>
        {
            lastStopped = true;
            return Task.CompletedTask;
        });
        coordinator.Register("hangs", _ => Task.Delay(Timeout.Infinite, CancellationToken.None));

        var report = await coordinator.ShutdownAsync();

        Assert.Equal(ShutdownStepStatus.TimedOut, report.Steps[0].Status);
        Assert.Equal(ShutdownStepStatus.Completed, report.Steps[1].Status);
        Assert.True(lastStopped);
    }

    [Fact]
    public async Task Component_honouring_cancellation_is_reported_as_timed_out()
    {
        using var coordinator = new LifecycleCoordinator(TimeProvider.System, ShortStep, TimeSpan.FromSeconds(5));
        coordinator.Register("cooperative", token => Task.Delay(Timeout.Infinite, token));

        var report = await coordinator.ShutdownAsync();

        Assert.Equal(ShutdownStepStatus.TimedOut, Assert.Single(report.Steps).Status);
    }

    [Fact]
    public async Task Failing_component_is_isolated_and_reported_by_type_only()
    {
        using var coordinator = new LifecycleCoordinator(TimeProvider.System);
        coordinator.Register("ok", _ => Task.CompletedTask);
        coordinator.Register("throws-sync", _ => throw new InvalidOperationException("secret detail"));
        coordinator.Register("throws-async", async _ =>
        {
            await Task.Yield();
            throw new IOException("disk");
        });

        var report = await coordinator.ShutdownAsync();

        Assert.Equal(ShutdownStepStatus.Faulted, report.Steps[0].Status);
        Assert.Equal(typeof(IOException).FullName, report.Steps[0].ErrorType);
        Assert.Equal(ShutdownStepStatus.Faulted, report.Steps[1].Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, report.Steps[1].ErrorType);
        Assert.Equal(ShutdownStepStatus.Completed, report.Steps[2].Status);
    }

    [Fact]
    public async Task Components_beyond_the_total_budget_are_skipped()
    {
        using var coordinator = new LifecycleCoordinator(TimeProvider.System, ShortStep, ShortStep);
        coordinator.Register("skipped", _ => Task.CompletedTask);
        coordinator.Register("hangs", _ => Task.Delay(Timeout.Infinite, CancellationToken.None));

        var report = await coordinator.ShutdownAsync();

        Assert.Equal(ShutdownStepStatus.TimedOut, report.Steps[0].Status);
        Assert.Equal(ShutdownStepStatus.Skipped, report.Steps[1].Status);
    }

    [Fact]
    public async Task Shutdown_runs_once_and_keeps_the_first_reason()
    {
        using var coordinator = new LifecycleCoordinator(TimeProvider.System);
        var calls = 0;
        coordinator.Register("counter", _ =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        });
        coordinator.RequestShutdown(ExitReason.Fatal);

        var first = coordinator.ShutdownAsync();
        var second = coordinator.ShutdownAsync();
        var report = await first;

        Assert.Same(first, second);
        Assert.Equal(1, calls);
        Assert.Equal(ExitReason.Fatal, report.Reason);
        Assert.Equal(ExitCodes.Fatal, report.ExitCode);
    }

    [Fact]
    public async Task Registering_after_shutdown_started_is_rejected()
    {
        using var coordinator = new LifecycleCoordinator(TimeProvider.System);
        await coordinator.ShutdownAsync();

        Assert.Throws<InvalidOperationException>(() => coordinator.Register("late", _ => Task.CompletedTask));
    }
}
