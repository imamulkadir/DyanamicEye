using DynamicIsland.Core.Lifecycle;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.Launcher;

internal static partial class LauncherLog
{
    [LoggerMessage(EventId = 2000, EventName = "LauncherStarted", Level = LogLevel.Information, Message = "Launcher started")]
    public static partial void Started(ILogger logger);

    [LoggerMessage(EventId = 2001, EventName = "AppRunEnded", Level = LogLevel.Information,
        Message = "App run {Run} (safe mode {SafeMode}) exited with code {ExitCode}; action {Action}")]
    public static partial void Step(ILogger logger, int run, bool safeMode, int exitCode, SupervisorAction action);

    [LoggerMessage(EventId = 2002, EventName = "LauncherFinished", Level = LogLevel.Information, Message = "Launcher finished with exit code {ExitCode}")]
    public static partial void Finished(ILogger logger, int exitCode);

    [LoggerMessage(EventId = 2003, EventName = "AppMissing", Level = LogLevel.Error, Message = "App executable not found (layout payload or next to the launcher)")]
    public static partial void AppMissing(ILogger logger);

    [LoggerMessage(EventId = 2004, EventName = "AppStartFailed", Level = LogLevel.Error, Message = "App could not be started (Win32 error {ErrorCode})")]
    public static partial void StartFailed(ILogger logger, int errorCode);

    [LoggerMessage(EventId = 2005, EventName = "UsingPreviousVersion", Level = LogLevel.Warning,
        Message = "Current app version is missing; running the previous version {Version}")]
    public static partial void UsingPreviousVersion(ILogger logger, string version);
}
