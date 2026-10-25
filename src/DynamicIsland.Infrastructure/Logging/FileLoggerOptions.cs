using Microsoft.Extensions.Logging;

namespace DynamicIsland.Infrastructure.Logging;

/// <param name="Directory">Log folder; normally <see cref="AppDataPaths.Logs"/>.</param>
public sealed record FileLoggerOptions(string Directory)
{
    public const string FilePrefix = "dynamicisland-";
    public const string FileExtension = ".log";

    public LogLevel MinimumLevel { get; init; } = LogLevel.Information;

    /// <summary>A file rolls to <c>.1.log</c>, <c>.2.log</c>… once it would exceed this size.</summary>
    public long MaxFileBytes { get; init; } = 5 * 1024 * 1024;

    /// <summary>Oldest files are deleted while the folder exceeds this size.</summary>
    public long MaxTotalBytes { get; init; } = 50 * 1024 * 1024;

    public TimeSpan Retention { get; init; } = TimeSpan.FromDays(14);

    /// <summary>Pending entries before the oldest are dropped (and counted) to protect callers.</summary>
    public int QueueCapacity { get; init; } = 4096;

    /// <summary>Longer messages are cut to keep single entries bounded.</summary>
    public int MaxMessageChars { get; init; } = 16 * 1024;
}
