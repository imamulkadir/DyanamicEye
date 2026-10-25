using Microsoft.Extensions.Logging;

namespace DynamicIsland.App;

/// <summary>App lifecycle log events. Messages carry no personal data.</summary>
internal static partial class AppLog
{
    [LoggerMessage(EventId = 1000, EventName = "AppStarted", Level = LogLevel.Information,
        Message = "Dynamic Island {Version} started on {OsVersion} ({Architecture})")]
    public static partial void Started(ILogger logger, string version, string osVersion, string architecture);

    [LoggerMessage(EventId = 1001, EventName = "AppExiting", Level = LogLevel.Information,
        Message = "Dynamic Island exiting with code {ExitCode}")]
    public static partial void Exiting(ILogger logger, int exitCode);
}
