using DynamicIsland.Core.Components;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Settings;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App;

/// <summary>App lifecycle log events. Messages carry no personal data: names, counts and exception types only.</summary>
internal static partial class AppLog
{
    [LoggerMessage(EventId = 1000, EventName = "AppStarted", Level = LogLevel.Information,
        Message = "Dynamic Island {Version} started on {OsVersion} ({Architecture})")]
    public static partial void Started(ILogger logger, string version, string osVersion, string architecture);

    [LoggerMessage(EventId = 1001, EventName = "AppExiting", Level = LogLevel.Information,
        Message = "Dynamic Island exiting: reason {Reason}, exit code {ExitCode}")]
    public static partial void Exiting(ILogger logger, ExitReason reason, int exitCode);

    [LoggerMessage(EventId = 1002, EventName = "PreviousSessionUnclean", Level = LogLevel.Warning,
        Message = "The previous session did not exit cleanly (crash, forced termination or power loss)")]
    public static partial void PreviousSessionUnclean(ILogger logger);

    [LoggerMessage(EventId = 1003, EventName = "SettingsLoaded", Level = LogLevel.Information,
        Message = "Settings loaded: outcome {Outcome}, {CorrectionCount} correction(s), read-only {ReadOnly}")]
    public static partial void SettingsLoaded(ILogger logger, SettingsLoadOutcome outcome, int correctionCount, bool readOnly);

    [LoggerMessage(EventId = 1004, EventName = "ShutdownRequested", Level = LogLevel.Information,
        Message = "Shutdown requested: {Reason}")]
    public static partial void ShutdownRequested(ILogger logger, ExitReason reason);

    [LoggerMessage(EventId = 1005, EventName = "ShutdownStepIncomplete", Level = LogLevel.Warning,
        Message = "Shutdown step {Step} {Status} after {ElapsedMs} ms ({ErrorType})")]
    public static partial void ShutdownStepIncomplete(ILogger logger, string step, ShutdownStepStatus status, long elapsedMs, string? errorType);

    [LoggerMessage(EventId = 1006, EventName = "ActivationRequested", Level = LogLevel.Information,
        Message = "Another launch asked this instance to show itself")]
    public static partial void ActivationRequested(ILogger logger);

    [LoggerMessage(EventId = 1007, EventName = "ActivationUnavailable", Level = LogLevel.Warning,
        Message = "Second-launch activation is unavailable ({ErrorType})")]
    public static partial void ActivationUnavailable(ILogger logger, string errorType);

    [LoggerMessage(EventId = 1008, EventName = "SafeModeActive", Level = LogLevel.Warning,
        Message = "Running in Safe Mode: optional components are not started")]
    public static partial void SafeModeActive(ILogger logger);

    [LoggerMessage(EventId = 1009, EventName = "ArgumentsIgnored", Level = LogLevel.Information,
        Message = "{Count} unrecognized command-line argument(s) ignored")]
    public static partial void ArgumentsIgnored(ILogger logger, int count);

    [LoggerMessage(EventId = 1020, EventName = "ComponentStatusChanged", Level = LogLevel.Information,
        Message = "Component {Component}: {State} (reason {Reason}, failures {Failures}, error {ErrorType})")]
    public static partial void ComponentStatusChanged(ILogger logger, string component, ComponentState state, string? reason, int failures, string? errorType);

    [LoggerMessage(EventId = 1021, EventName = "NoticePublished", Level = LogLevel.Information,
        Message = "Notice {Severity}: {MessageKey} ({Subject})")]
    public static partial void NoticePublished(ILogger logger, NoticeSeverity severity, string messageKey, string? subject);

    /// <remarks>Monitor ids contain hardware identifiers and are never logged (ADR 0008).</remarks>
    [LoggerMessage(EventId = 1030, EventName = "IslandPlaced", Level = LogLevel.Information,
        Message = "Island placed: primary monitor {IsPrimary}, fallback {UsedFallback}, scale {DpiScale}, size {Width}x{Height} px")]
    public static partial void IslandPlaced(ILogger logger, bool isPrimary, bool usedFallback, double dpiScale, int width, int height);

    [LoggerMessage(EventId = 1032, EventName = "FullscreenDetectionStarted", Level = LogLevel.Information,
        Message = "Fullscreen detection started with {HookCount} event hook(s)")]
    public static partial void FullscreenDetectionStarted(ILogger logger, int hookCount);

    [LoggerMessage(EventId = 1031, EventName = "FullscreenHide", Level = LogLevel.Information,
        Message = "Fullscreen app on the island's monitor: island hidden {Hidden}")]
    public static partial void FullscreenHide(ILogger logger, bool hidden);

    [LoggerMessage(EventId = 1040, EventName = "TrayUnavailable", Level = LogLevel.Warning,
        Message = "The notification-area icon could not be added (no shell tray); the island remains usable without it")]
    public static partial void TrayUnavailable(ILogger logger);

    [LoggerMessage(EventId = 1041, EventName = "RestartRequested", Level = LogLevel.Information,
        Message = "Restart requested (safe mode {SafeMode})")]
    public static partial void RestartRequested(ILogger logger, bool safeMode);

    [LoggerMessage(EventId = 1042, EventName = "RestartUnavailable", Level = LogLevel.Warning,
        Message = "Restart is unavailable: the launcher is not installed next to the app; restart Dynamic Island manually")]
    public static partial void RestartUnavailable(ILogger logger);

    [LoggerMessage(EventId = 1043, EventName = "SettingsNotSaved", Level = LogLevel.Warning,
        Message = "Settings change applied for this session only ({Reason})")]
    public static partial void SettingsNotSaved(ILogger logger, string reason);

    [LoggerMessage(EventId = 1044, EventName = "OpenLogsFailed", Level = LogLevel.Warning,
        Message = "The logs folder could not be opened ({ErrorType})")]
    public static partial void OpenLogsFailed(ILogger logger, string errorType);

    [LoggerMessage(EventId = 1045, EventName = "ComponentPauseIncomplete", Level = LogLevel.Warning,
        Message = "Pausing or resuming components did not finish in time")]
    public static partial void ComponentPauseIncomplete(ILogger logger);

    [LoggerMessage(EventId = 1010, EventName = "UnhandledException", Level = LogLevel.Critical,
        Message = "Unhandled exception on {Source}; the app will exit")]
    public static partial void UnhandledException(ILogger logger, Exception exception, string source);

    [LoggerMessage(EventId = 1011, EventName = "UnobservedTaskException", Level = LogLevel.Error,
        Message = "A background task failed without being observed")]
    public static partial void UnobservedTaskException(ILogger logger, Exception exception);
}
