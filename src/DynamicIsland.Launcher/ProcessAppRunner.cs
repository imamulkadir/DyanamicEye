using System.Diagnostics;
using DynamicIsland.Core.Lifecycle;

namespace DynamicIsland.Launcher;

/// <summary>Starts the app as a child process with only allow-listed arguments and waits for it to exit.</summary>
internal sealed class ProcessAppRunner(string appPath) : IAppProcessRunner
{
    public async Task<int> RunAsync(bool safeMode, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(appPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(appPath)!,
        };

        if (safeMode)
        {
            startInfo.ArgumentList.Add(LaunchArguments.SafeModeSwitch);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The app process could not be started.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }
}
