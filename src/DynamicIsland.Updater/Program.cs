using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Updates;
using DynamicIsland.Infrastructure;
using DynamicIsland.Infrastructure.Logging;
using DynamicIsland.Infrastructure.Updates;

namespace DynamicIsland.Updater;

/// <summary>
/// Entry point: <c>DynamicIsland.Updater.exe --request &lt;path&gt;</c> (ADR 0035). The request must be the per-user
/// <c>updates\update-request.json</c>; anything else is refused. Exit codes: 0 committed, 2 rolled back, 3 aborted
/// (no changes), 4 invalid arguments.
/// </summary>
internal static class Program
{
    private const string RequestSwitch = "--request";
    private const int Committed = 0;
    private const int RolledBack = 2;
    private const int Aborted = 3;
    private const int InvalidArguments = 4;

    private static int Main(string[] args)
    {
        var updates = Path.Combine(AppDataPaths.Root, "updates");
        using var logs = new FileLoggerProvider(
            new FileLoggerOptions(AppDataPaths.Logs) { FilePrefix = "updater-" },
            LogRedactor.ForCurrentEnvironment(),
            TimeProvider.System);
        var logger = logs.CreateLogger("DynamicIsland.Updater");

        if (ReadRequest(args, updates) is not { } request)
        {
            UpdaterLog.InvalidArguments(logger);
            return InvalidArguments;
        }

        var swap = new UpdateSwap(updates, AppDataPaths.Data, new WindowsUpdaterProcesses(), new UninstallEntryVersion(), logger);
        var result = swap.RunAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        return result.Outcome switch
        {
            SwapOutcome.Committed => Committed,
            SwapOutcome.RolledBack => RolledBack,
            _ => Aborted,
        };
    }

    /// <returns>The request, or <c>null</c> for anything but <c>--request</c> with the per-user request file.</returns>
    private static UpdateRequest? ReadRequest(string[] args, string updates)
    {
        if (args.Length != 2 || !string.Equals(args[0], RequestSwitch, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(args[1]))
        {
            return null;
        }

        try
        {
            return string.Equals(Path.GetFullPath(args[1]), Path.Combine(Path.GetFullPath(updates), UpdateRequest.FileName), StringComparison.OrdinalIgnoreCase)
                && new FileInfo(args[1]) is { Exists: true, Length: <= UpdateRequest.MaxBytes } file
                ? UpdateRequest.Parse(File.ReadAllText(file.FullName))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
