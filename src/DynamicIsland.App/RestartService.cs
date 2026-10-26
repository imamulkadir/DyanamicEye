using System.Diagnostics;
using System.Globalization;
using System.IO;
using DynamicIsland.Core.Lifecycle;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App;

/// <summary>
/// Restarts the app normally or into Safe Mode (ADR 0013).
/// <list type="bullet">
/// <item>Supervised by the launcher (<see cref="LauncherContract.SupervisedVariable"/>): exit with code 3/4; the launcher relaunches.</item>
/// <item>Started directly: start the launcher with <c>--after-pid</c> (it waits for this process, so the single-instance
/// lock is free) and exit. Without a launcher next to the app the restart is refused and logged.</item>
/// </list>
/// </summary>
public sealed class RestartService(LifecycleCoordinator lifecycle, ILogger<RestartService> logger)
{
    /// <returns><c>false</c> when the restart cannot be arranged (no launcher next to the app).</returns>
    public bool Restart(bool safeMode)
    {
        if (Environment.GetEnvironmentVariable(LauncherContract.SupervisedVariable) != "1")
        {
            var launcher = Path.Combine(AppContext.BaseDirectory, LauncherContract.LauncherExecutable);
            if (!File.Exists(launcher))
            {
                AppLog.RestartUnavailable(logger);
                return false;
            }

            var startInfo = new ProcessStartInfo(launcher) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
            startInfo.ArgumentList.Add(LauncherContract.AfterPidSwitch);
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            if (safeMode)
            {
                startInfo.ArgumentList.Add(LaunchArguments.SafeModeSwitch);
            }

            Process.Start(startInfo)?.Dispose();
        }

        AppLog.RestartRequested(logger, safeMode);
        lifecycle.RequestShutdown(safeMode ? ExitReason.RestartInSafeMode : ExitReason.Restart);
        return true;
    }
}
