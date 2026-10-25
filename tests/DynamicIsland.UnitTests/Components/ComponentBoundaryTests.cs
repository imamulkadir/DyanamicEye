using DynamicIsland.Core.Components;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Notifications;

namespace DynamicIsland.UnitTests.Components;

public sealed class ComponentBoundaryTests : IDisposable
{
    private static readonly RetryPolicy FastRetries = new() { BaseDelay = TimeSpan.FromMilliseconds(1), MaxDelay = TimeSpan.FromMilliseconds(5) };

    private readonly LifecycleCoordinator _lifecycle = new(TimeProvider.System, TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(2));
    private readonly InMemoryNotificationService _notices = new(TimeProvider.System);

    public void Dispose() => _lifecycle.Dispose();

    [Fact]
    public async Task Failing_component_is_isolated_while_others_keep_running()
    {
        using var host = CreateHost();
        var faulty = new ScriptedComponent("faulty", _ => throw new InvalidOperationException("simulated widget bug"));
        var healthy = new ScriptedComponent("healthy", RunHealthyAsync);
        host.Add(faulty, optional: true, FastRetries);
        host.Add(healthy, optional: true, FastRetries);

        host.StartAll();
        await WaitUntilAsync(() => State(host, "faulty") == ComponentState.Failed);

        Assert.Equal(1 + FastRetries.MaxRecoverableAttempts, faulty.Runs);
        Assert.Equal(ComponentState.Healthy, State(host, "healthy"));
        Assert.Null(_lifecycle.Reason);
        var failed = Single(host, "faulty");
        Assert.Equal(ReasonCodes.RecoverableFailure, failed.ReasonCode);
        Assert.Equal(typeof(InvalidOperationException).FullName, failed.LastErrorType);
        Assert.Contains(new UserNotice(NoticeSeverity.Warning, "component.failed", "faulty", "open-diagnostics"), _notices.Active);
    }

    [Fact]
    public async Task Transient_failures_retry_until_recovered_and_notify_once()
    {
        using var host = CreateHost();
        var component = new ScriptedComponent("network", async (context, run, token) =>
        {
            if (run <= 5)
            {
                throw new TimeoutException();
            }

            await RunHealthyAsync(context, token);
        });
        host.Add(component, optional: true, FastRetries);

        host.StartAll();
        await WaitUntilAsync(() => State(host, "network") == ComponentState.Healthy);

        Assert.Equal(6, component.Runs);
        Assert.Single(_notices.Active, n => n.MessageKey == "component.retrying");
    }

    [Fact]
    public async Task Healthy_report_resets_the_failure_count()
    {
        using var host = CreateHost();
        var component = new ScriptedComponent("flaky", async (context, run, token) =>
        {
            // Two cycles of "works for a moment, then fails", each below the recoverable limit on its own.
            if (run is 2 or 4)
            {
                context.ReportHealthy();
            }

            if (run < 6)
            {
                throw new InvalidOperationException();
            }

            await RunHealthyAsync(context, token);
        });
        host.Add(component, optional: true, FastRetries with { MaxRecoverableAttempts = 3 });

        host.StartAll();
        await WaitUntilAsync(() => component.Runs >= 6 && State(host, "flaky") == ComponentState.Healthy);

        Assert.DoesNotContain(_notices.Active, n => n.MessageKey == "component.failed");
    }

    [Fact]
    public async Task Fatal_failure_requests_a_fatal_shutdown()
    {
        using var host = CreateHost();
        host.Add(new ScriptedComponent("device", _ => throw new ComponentFailureException(FailureKind.Fatal, "device-lost")), optional: false, FastRetries);

        host.StartAll();
        await WaitUntilAsync(() => _lifecycle.Reason is not null);

        Assert.Equal(ExitReason.Fatal, _lifecycle.Reason);
        var status = Single(host, "device");
        Assert.Equal(ComponentState.Failed, status.State);
        Assert.Equal("device-lost", status.ReasonCode);
    }

    [Fact]
    public void Optional_components_do_not_start_in_safe_mode()
    {
        using var host = CreateHost(new LaunchArguments(SafeMode: true, IgnoredCount: 0));
        var optional = new ScriptedComponent("widget", RunHealthyAsync);
        var required = new ScriptedComponent("core-service", RunHealthyAsync);
        host.Add(optional, optional: true);
        host.Add(required, optional: false);

        host.StartAll();

        Assert.Equal(ComponentState.SkippedSafeMode, State(host, "widget"));
        Assert.NotEqual(ComponentState.SkippedSafeMode, State(host, "core-service"));
    }

    [Fact]
    public async Task App_shutdown_stops_components_through_the_lifecycle()
    {
        using var host = CreateHost();
        host.Add(new ScriptedComponent("widget", RunHealthyAsync), optional: true);
        host.StartAll();
        await WaitUntilAsync(() => State(host, "widget") == ComponentState.Healthy);

        var report = await _lifecycle.ShutdownAsync();

        Assert.Equal(ShutdownStepStatus.Completed, Assert.Single(report.Steps).Status);
        Assert.Equal(ComponentState.Stopped, State(host, "widget"));
    }

    [Fact]
    public async Task Component_ignoring_cancellation_cannot_block_shutdown()
    {
        using var host = CreateHost();
        host.Add(new ScriptedComponent("stuck", async (context, _, _) =>
        {
            context.ReportHealthy();
            await Task.Delay(Timeout.Infinite, CancellationToken.None);
        }), optional: true);
        host.StartAll();
        await WaitUntilAsync(() => State(host, "stuck") == ComponentState.Healthy);

        var report = await _lifecycle.ShutdownAsync();

        Assert.Equal(ShutdownStepStatus.TimedOut, Assert.Single(report.Steps).Status);
    }

    [Fact]
    public void Invalid_or_duplicate_component_names_are_rejected()
    {
        using var host = CreateHost();
        host.Add(new ScriptedComponent("clock", RunHealthyAsync), optional: true);

        Assert.Throws<ArgumentException>(() => host.Add(new ScriptedComponent("clock", RunHealthyAsync), optional: true));
        Assert.Throws<ArgumentException>(() => host.Add(new ScriptedComponent("Bad Name!", RunHealthyAsync), optional: true));
    }

    private ComponentHost CreateHost(LaunchArguments? launch = null) =>
        new(_lifecycle, _notices, new DefaultFailureClassifier(), launch ?? LaunchArguments.Default, TimeProvider.System);

    private static ComponentStatus Single(ComponentHost host, string name) => host.Statuses.Single(s => s.Name == name);

    private static ComponentState State(ComponentHost host, string name) => Single(host, name).State;

    private static async Task RunHealthyAsync(IComponentContext context, CancellationToken token)
    {
        context.ReportHealthy();
        await Task.Delay(Timeout.Infinite, token);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition not reached within 10 s.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class ScriptedComponent : IIsolatedComponent
    {
        private readonly Func<IComponentContext, int, CancellationToken, Task> _run;
        private int _runs;

        public ScriptedComponent(string name, Func<IComponentContext, int, CancellationToken, Task> run)
        {
            Name = name;
            _run = run;
        }

        public ScriptedComponent(string name, Func<IComponentContext, CancellationToken, Task> run)
            : this(name, (context, _, token) => run(context, token))
        {
        }

        public ScriptedComponent(string name, Func<int, Task> run)
            : this(name, (_, attempt, _) => run(attempt))
        {
        }

        public string Name { get; }

        public int Runs => Volatile.Read(ref _runs);

        public Task RunAsync(IComponentContext context, CancellationToken cancellationToken) =>
            _run(context, Interlocked.Increment(ref _runs), cancellationToken);
    }
}
