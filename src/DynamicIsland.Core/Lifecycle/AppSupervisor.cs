namespace DynamicIsland.Core.Lifecycle;

/// <summary>Persists crash times across launcher runs.</summary>
public interface ICrashStateStore
{
    /// <summary>Adds a crash and returns the recent crashes (inside <see cref="CrashPolicy.Window"/>), oldest first.</summary>
    IReadOnlyList<DateTimeOffset> RecordCrash(DateTimeOffset now);
}

/// <summary>Starts the app process once and waits for it.</summary>
public interface IAppProcessRunner
{
    /// <returns>The process exit code.</returns>
    Task<int> RunAsync(bool safeMode, CancellationToken cancellationToken);
}

/// <summary>Tells the user that automatic recovery gave up and how to recover manually.</summary>
public interface IRecoveryNotifier
{
    void NotifyStopped();
}

public enum SupervisorAction
{
    /// <summary>Clean exit (Exit, sign-out, second instance): never relaunched.</summary>
    Finished,
    Relaunch,
    RelaunchInSafeMode,
    Stop,
}

public sealed record SupervisorStep(int Run, bool SafeMode, int ExitCode, SupervisorAction Action);

/// <summary>
/// Launcher loop (guide §4.5, D03). Exit code 0 ends supervision; <see cref="ExitCodes.RestartRequested"/> relaunches
/// normally and <see cref="ExitCodes.RestartInSafeModeRequested"/> in Safe Mode, without counting a crash (bounded);
/// anything else is a crash decided by <see cref="CrashPolicy"/>.
/// </summary>
public sealed class AppSupervisor(IAppProcessRunner runner, ICrashStateStore crashStore, IRecoveryNotifier notifier, TimeProvider timeProvider)
{
    /// <summary>Restart requests beyond this per launcher lifetime count as crashes (protects against restart loops).</summary>
    public const int MaxRestartRequests = 5;

    /// <param name="startInSafeMode">First run in Safe Mode (launcher started with <c>--safe-mode</c>).</param>
    /// <returns>0 after a clean exit; otherwise the exit code of the last run.</returns>
    public async Task<int> RunAsync(Action<SupervisorStep>? onStep, CancellationToken cancellationToken, bool startInSafeMode = false)
    {
        var safeMode = startInSafeMode;
        var restartRequests = 0;

        for (var run = 1; ; run++)
        {
            var exitCode = await runner.RunAsync(safeMode, cancellationToken).ConfigureAwait(false);
            var action = Decide(exitCode, safeMode, ref restartRequests);
            onStep?.Invoke(new SupervisorStep(run, safeMode, exitCode, action));

            switch (action)
            {
                case SupervisorAction.Finished:
                    return ExitCodes.Success;
                case SupervisorAction.Stop:
                    notifier.NotifyStopped();
                    return exitCode;
                default:
                    safeMode = action == SupervisorAction.RelaunchInSafeMode;
                    break;
            }
        }
    }

    private SupervisorAction Decide(int exitCode, bool safeMode, ref int restartRequests)
    {
        if (exitCode == ExitCodes.Success)
        {
            return SupervisorAction.Finished;
        }

        if (exitCode is ExitCodes.RestartRequested or ExitCodes.RestartInSafeModeRequested && ++restartRequests <= MaxRestartRequests)
        {
            return exitCode == ExitCodes.RestartInSafeModeRequested ? SupervisorAction.RelaunchInSafeMode : SupervisorAction.Relaunch;
        }

        var now = timeProvider.GetUtcNow();
        var recentCrashes = crashStore.RecordCrash(now);
        return CrashPolicy.Decide(recentCrashes, now, safeMode) switch
        {
            CrashDecision.Restart => SupervisorAction.Relaunch,
            CrashDecision.RestartInSafeMode => SupervisorAction.RelaunchInSafeMode,
            _ => SupervisorAction.Stop,
        };
    }
}
