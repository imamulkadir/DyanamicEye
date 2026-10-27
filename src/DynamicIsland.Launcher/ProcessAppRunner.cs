using System.Diagnostics;
using DynamicIsland.Core.Lifecycle;

namespace DynamicIsland.Launcher;

/// <summary>Starts the app as a child process with only allow-listed arguments and waits for it to exit.</summary>
/// <param name="startupLaunch">Sign-in start: only the first app start gets <c>--startup</c>; crash restarts do not (ADR 0015).</param>
internal sealed class ProcessAppRunner(string appPath, bool startupLaunch = false) : IAppProcessRunner
{
    private bool _startupPending = startupLaunch;

    public async Task<int> RunAsync(bool safeMode, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(appPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(appPath)!,
        };
        startInfo.Environment[LauncherContract.SupervisedVariable] = "1";
        startInfo.Environment[LauncherContract.LauncherPidVariable] = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (safeMode)
        {
            startInfo.ArgumentList.Add(LaunchArguments.SafeModeSwitch);
        }

        if (_startupPending)
        {
            startInfo.ArgumentList.Add(LaunchArguments.StartupSwitch);
            _startupPending = false;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The app process could not be started.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }
}
