using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Infrastructure;
using DynamicIsland.Infrastructure.Lifecycle;
using DynamicIsland.Infrastructure.Logging;

namespace DynamicIsland.Launcher;

/// <summary>
/// Entry point for sign-in startup, shortcuts and restarts. Accepts only <c>--safe-mode</c>, <c>--startup</c> (sign-in
/// start: wait <see cref="LauncherContract.StartupDelay"/> first, ADR 0015) and <c>--after-pid N</c> (wait for a previous
/// app instance to exit first); anything else is ignored.
/// Exit code: 0 after a clean app exit; otherwise the app's last exit code, or 1 when the app cannot be started.
/// </summary>
internal static class Program
{
    private const string AppExecutableName = "DynamicIsland.exe";
    private const string LogFilePrefix = "launcher-";

    private static readonly TimeSpan PreviousInstanceTimeout = TimeSpan.FromSeconds(15);

    private static int Main(string[] args)
    {
        var arguments = LauncherArguments.Parse(args);
        var notifier = new MessageBoxRecoveryNotifier(AppDataPaths.Logs);
        using var logs = new FileLoggerProvider(
            new FileLoggerOptions(AppDataPaths.Logs) { FilePrefix = LogFilePrefix },
            LogRedactor.ForCurrentEnvironment(),
            TimeProvider.System);
        var logger = logs.CreateLogger("DynamicIsland.Launcher");

        var appPath = Path.Combine(AppContext.BaseDirectory, AppExecutableName);
        if (!File.Exists(appPath))
        {
            LauncherLog.AppMissing(logger);
            MessageBoxRecoveryNotifier.NotifyAppMissing();
            return ExitCodes.Fatal;
        }

        var supervisor = new AppSupervisor(
            new ProcessAppRunner(appPath, arguments.Startup),
            new JsonCrashStateStore(AppDataPaths.Data),
            notifier,
            TimeProvider.System);

        LauncherLog.Started(logger);
        if (arguments.AfterProcessId is { } previous)
        {
            WaitForPreviousInstance(previous);
        }

        if (arguments.Startup)
        {
            // Sign-in: let the shell and the user's other startup apps settle first.
            Thread.Sleep(LauncherContract.StartupDelay);
        }

        try
        {
            var exitCode = supervisor
                .RunAsync(step => LauncherLog.Step(logger, step.Run, step.SafeMode, step.ExitCode, step.Action), CancellationToken.None, arguments.SafeMode)
                .GetAwaiter()
                .GetResult();
            LauncherLog.Finished(logger, exitCode);
            return exitCode;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // The app could not be started at all (blocked by policy, missing dependency, access denied).
            LauncherLog.StartFailed(logger, ex.NativeErrorCode);
            MessageBoxRecoveryNotifier.NotifyAppMissing();
            return ExitCodes.Fatal;
        }
    }

    /// <summary>Restart of an unsupervised app: let the old instance release its single-instance lock first (bounded).</summary>
    private static void WaitForPreviousInstance(int processId)
    {
        try
        {
            using var previous = System.Diagnostics.Process.GetProcessById(processId);
            previous.WaitForExit(PreviousInstanceTimeout);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }
}
