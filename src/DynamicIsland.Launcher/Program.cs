using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Distribution;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Infrastructure;
using DynamicIsland.Infrastructure.Lifecycle;
using DynamicIsland.Infrastructure.Logging;

namespace DynamicIsland.Launcher;

/// <summary>
/// Entry point for sign-in startup, shortcuts and restarts. In a release layout it runs the payload named by
/// <c>current.json</c> (falling back to the previous one), otherwise the app next to it (ADR 0030). Accepts only <c>--safe-mode</c>, <c>--startup</c> (sign-in
/// start: wait <see cref="LauncherContract.StartupDelay"/> first, ADR 0015) and <c>--after-pid N</c> (wait for a previous
/// app instance to exit first); anything else is ignored.
/// Exit code: 0 after a clean app exit; otherwise the app's last exit code, or 1 when the app cannot be started.
/// </summary>
internal static class Program
{
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

        var app = AppLayout.ResolveApp(AppContext.BaseDirectory, ReadPointer(), File.Exists, Environment.ProcessPath);
        if (app is null)
        {
            LauncherLog.AppMissing(logger);
            MessageBoxRecoveryNotifier.NotifyAppMissing();
            return ExitCodes.Fatal;
        }

        if (app.UsedPrevious)
        {
            LauncherLog.UsingPreviousVersion(logger, app.Version ?? string.Empty);
        }

        var appPath = app.AppPath;

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

    /// <returns>The layout pointer, or <c>null</c> without one (development layout) or when it cannot be read.</returns>
    private static string? ReadPointer()
    {
        var path = Path.Combine(AppContext.BaseDirectory, AppLayout.PointerFileName);
        try
        {
            return File.Exists(path) && new FileInfo(path).Length <= AppLayout.MaxPointerBytes ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
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
