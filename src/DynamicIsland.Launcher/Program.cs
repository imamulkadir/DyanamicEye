using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Infrastructure;
using DynamicIsland.Infrastructure.Lifecycle;
using DynamicIsland.Infrastructure.Logging;

namespace DynamicIsland.Launcher;

/// <summary>
/// Entry point for sign-in startup and shortcuts. Accepts no arguments (anything passed is ignored).
/// Exit code: 0 after a clean app exit; otherwise the app's last exit code, or 1 when the app cannot be started.
/// </summary>
internal static class Program
{
    private const string AppExecutableName = "DynamicIsland.exe";
    private const string LogFilePrefix = "launcher-";

    private static int Main()
    {
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
            new ProcessAppRunner(appPath),
            new JsonCrashStateStore(AppDataPaths.Data),
            notifier,
            TimeProvider.System);

        LauncherLog.Started(logger);
        try
        {
            var exitCode = supervisor
                .RunAsync(step => LauncherLog.Step(logger, step.Run, step.SafeMode, step.ExitCode, step.Action), CancellationToken.None)
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
}
