using DynamicIsland.Core.Lifecycle;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.Infrastructure.Lifecycle;

/// <summary>
/// Empty <c>session.running</c> file in the data folder: present while the app runs, removed on a clean exit.
/// Marker I/O problems are logged and never stop the app.
/// </summary>
public sealed partial class FileSessionStateMarker(string dataDirectory, ILogger<FileSessionStateMarker> logger) : ISessionStateMarker
{
    public const string FileName = "session.running";

    private readonly string _path = Path.Combine(Path.GetFullPath(dataDirectory), FileName);

    public bool BeginSession()
    {
        try
        {
            var previousWasUnclean = File.Exists(_path);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(_path, []);
            return previousWasUnclean;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogMarkerFailed(logger, ex.GetType().Name);
            return false;
        }
    }

    public void EndSessionCleanly()
    {
        try
        {
            File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogMarkerFailed(logger, ex.GetType().Name);
        }
    }

    [LoggerMessage(EventId = 1100, EventName = "SessionMarkerFailed", Level = LogLevel.Warning,
        Message = "Session marker could not be updated ({ErrorType}); crash detection is unavailable for this session")]
    private static partial void LogMarkerFailed(ILogger logger, string errorType);
}
