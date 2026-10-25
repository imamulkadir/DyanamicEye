using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Infrastructure.Lifecycle;
using DynamicIsland.UnitTests.TestSupport;

namespace DynamicIsland.UnitTests.Lifecycle;

public sealed class CrashPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void First_and_second_crash_in_the_window_restart()
    {
        Assert.Equal(CrashDecision.Restart, CrashPolicy.Decide([Now], Now, crashedInSafeMode: false));
        Assert.Equal(CrashDecision.Restart, CrashPolicy.Decide([Now.AddMinutes(-1), Now], Now, crashedInSafeMode: false));
    }

    [Fact]
    public void Third_crash_in_the_window_restarts_in_safe_mode()
    {
        DateTimeOffset[] crashes = [Now.AddMinutes(-4), Now.AddMinutes(-2), Now];

        Assert.Equal(CrashDecision.RestartInSafeMode, CrashPolicy.Decide(crashes, Now, crashedInSafeMode: false));
    }

    [Fact]
    public void Crash_in_safe_mode_stops()
    {
        Assert.Equal(CrashDecision.Stop, CrashPolicy.Decide([Now], Now, crashedInSafeMode: true));
    }

    [Fact]
    public void Crashes_outside_the_window_or_in_the_future_are_ignored()
    {
        DateTimeOffset[] crashes = [Now.AddMinutes(-30), Now.AddMinutes(-6), Now.AddHours(2), Now];

        Assert.Equal(CrashDecision.Restart, CrashPolicy.Decide(crashes, Now, crashedInSafeMode: false));
    }
}

public sealed class AppSupervisorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly FixedTimeProvider _time = new(Start);
    private readonly RecordingNotifier _notifier = new();
    private readonly InMemoryCrashStore _store = new();

    [Theory]
    [InlineData(ExitCodes.Success)]
    public async Task Clean_exit_is_never_relaunched(int exitCode)
    {
        var runner = new ScriptedRunner(exitCode);

        var result = await CreateSupervisor(runner).RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, result);
        Assert.Single(runner.Runs);
        Assert.Empty(_store.Crashes);
        Assert.False(_notifier.Stopped);
    }

    [Fact]
    public async Task Crash_loop_restarts_twice_then_safe_mode_then_stops()
    {
        var runner = new ScriptedRunner(ExitCodes.Fatal, ExitCodes.Fatal, ExitCodes.Fatal, ExitCodes.Fatal);
        var steps = new List<SupervisorStep>();

        var result = await CreateSupervisor(runner).RunAsync(steps.Add, TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Fatal, result);
        Assert.Equal([false, false, false, true], runner.Runs);
        Assert.Equal(
            [SupervisorAction.Relaunch, SupervisorAction.Relaunch, SupervisorAction.RelaunchInSafeMode, SupervisorAction.Stop],
            steps.Select(s => s.Action));
        Assert.True(_notifier.Stopped);
    }

    [Fact]
    public async Task Safe_mode_run_that_exits_cleanly_ends_supervision()
    {
        var runner = new ScriptedRunner(ExitCodes.Fatal, ExitCodes.Fatal, ExitCodes.Fatal, ExitCodes.Success);

        var result = await CreateSupervisor(runner).RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, result);
        Assert.Equal([false, false, false, true], runner.Runs);
        Assert.False(_notifier.Stopped);
    }

    [Fact]
    public async Task Crashes_spread_beyond_the_window_keep_restarting_normally()
    {
        var runner = new ScriptedRunner(ExitCodes.Fatal, ExitCodes.Fatal, ExitCodes.Fatal, ExitCodes.Success)
        {
            OnRun = () => _time.UtcNow += TimeSpan.FromMinutes(3),
        };

        await CreateSupervisor(runner).RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal([false, false, false, false], runner.Runs);
    }

    [Fact]
    public async Task Abnormal_termination_codes_count_as_crashes()
    {
        var runner = new ScriptedRunner(-1, unchecked((int)0xC0000005), 0);

        await CreateSupervisor(runner).RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(2, _store.Crashes.Count);
    }

    [Fact]
    public async Task Restart_request_relaunches_normally_without_counting_a_crash()
    {
        var runner = new ScriptedRunner(ExitCodes.Fatal, ExitCodes.Fatal, ExitCodes.Fatal, ExitCodes.RestartRequested, ExitCodes.Success);

        await CreateSupervisor(runner).RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal([false, false, false, true, false], runner.Runs);
        Assert.Equal(3, _store.Crashes.Count);
    }

    [Fact]
    public async Task Endless_restart_requests_are_treated_as_crashes()
    {
        var codes = Enumerable.Repeat(ExitCodes.RestartRequested, AppSupervisor.MaxRestartRequests + 10).ToArray();
        var runner = new ScriptedRunner(codes);

        var result = await CreateSupervisor(runner).RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.RestartRequested, result);
        Assert.Equal(AppSupervisor.MaxRestartRequests + 4, runner.Runs.Count);
        Assert.True(_notifier.Stopped);
    }

    private AppSupervisor CreateSupervisor(ScriptedRunner runner) => new(runner, _store, _notifier, _time);

    private sealed class ScriptedRunner(params int[] exitCodes) : IAppProcessRunner
    {
        private int _next;

        public List<bool> Runs { get; } = [];

        public Action? OnRun { get; init; }

        public Task<int> RunAsync(bool safeMode, CancellationToken cancellationToken)
        {
            Runs.Add(safeMode);
            OnRun?.Invoke();
            Assert.True(_next < exitCodes.Length, "Supervisor ran the app more often than scripted.");
            return Task.FromResult(exitCodes[_next++]);
        }
    }

    private sealed class InMemoryCrashStore : ICrashStateStore
    {
        public List<DateTimeOffset> Crashes { get; } = [];

        public IReadOnlyList<DateTimeOffset> RecordCrash(DateTimeOffset now)
        {
            Crashes.Add(now);
            return Crashes.Where(c => CrashPolicy.IsRecent(c, now)).ToList();
        }
    }

    private sealed class RecordingNotifier : IRecoveryNotifier
    {
        public bool Stopped { get; private set; }

        public void NotifyStopped() => Stopped = true;
    }
}

public sealed class JsonCrashStateStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void Crashes_persist_across_store_instances()
    {
        new JsonCrashStateStore(_directory.Path).RecordCrash(Now.AddMinutes(-1));

        var recent = new JsonCrashStateStore(_directory.Path).RecordCrash(Now);

        Assert.Equal([Now.AddMinutes(-1), Now], recent);
        Assert.Equal([JsonCrashStateStore.FileName], _directory.FileNames());
    }

    [Fact]
    public void Old_crashes_are_pruned()
    {
        var store = new JsonCrashStateStore(_directory.Path);
        store.RecordCrash(Now.AddMinutes(-10));

        Assert.Equal([Now], store.RecordCrash(Now));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"crashes\":[\"not a date\", 5, null]}")]
    public void Corrupt_history_is_replaced(string content)
    {
        File.WriteAllText(_directory.Combine(JsonCrashStateStore.FileName), content);

        var recent = new JsonCrashStateStore(_directory.Path).RecordCrash(Now);

        Assert.Equal([Now], recent);

        // The rewritten file is valid again: a fresh store sees the earlier crash.
        Assert.Equal(2, new JsonCrashStateStore(_directory.Path).RecordCrash(Now.AddSeconds(1)).Count);
    }

    [Fact]
    public void Unwritable_folder_keeps_history_in_memory()
    {
        var blocker = _directory.Combine("blocked");
        File.WriteAllText(blocker, "file in place of folder");
        var store = new JsonCrashStateStore(Path.Combine(blocker, "data"));

        store.RecordCrash(Now.AddMinutes(-1));
        var recent = store.RecordCrash(Now);

        Assert.Equal(2, recent.Count);
    }
}

public sealed class LaunchArgumentsTests
{
    [Fact]
    public void No_arguments_is_a_normal_start()
    {
        Assert.Equal(LaunchArguments.Default, LaunchArguments.Parse([]));
    }

    [Theory]
    [InlineData("--safe-mode")]
    [InlineData("--SAFE-MODE")]
    public void Safe_mode_switch_is_recognized(string argument)
    {
        Assert.True(LaunchArguments.Parse([argument]).SafeMode);
    }

    [Fact]
    public void Unknown_arguments_are_counted_and_ignored()
    {
        var parsed = LaunchArguments.Parse(["--safe-mode", "--evil", @"C:\payload.dll", "-safe-mode"]);

        Assert.True(parsed.SafeMode);
        Assert.Equal(3, parsed.IgnoredCount);
    }
}
