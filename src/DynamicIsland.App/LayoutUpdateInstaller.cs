using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Updates;
using DynamicIsland.Infrastructure.Updates;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App;

/// <summary>
/// Install step of an approved update (guide P06-06, ADR 0035): writes the hand-over request, starts the separate updater
/// from this payload and exits, so the updater can swap versions and roll back if the new one does not start.
/// </summary>
public sealed class LayoutUpdateInstaller(UpdateHandover handover, LifecycleCoordinator lifecycle, ILogger<LayoutUpdateInstaller> logger) : IUpdateInstaller
{
    public const string UpdaterExecutable = "DynamicIsland.Updater.exe";

    private const string NotPossible = "This copy can't update itself. Download the new version from the release page.";
    private const string ReadOnly = "This copy's folder can't be changed, so it can't update itself. Download the new version from the release page.";

    public Task<string?> InstallAsync(StagedPackage package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        var updater = Path.Combine(AppContext.BaseDirectory, UpdaterExecutable);
        if (!File.Exists(updater))
        {
            return Task.FromResult<string?>(NotPossible);
        }

        var launcherPid = Environment.GetEnvironmentVariable(LauncherContract.SupervisedVariable) == "1"
            && int.TryParse(Environment.GetEnvironmentVariable(LauncherContract.LauncherPidVariable), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
            && pid > 0 ? pid : (int?)null;
        switch (handover.Prepare(package, AppContext.BaseDirectory, Environment.ProcessId, launcherPid))
        {
            case null:
                break;
            case "layout-read-only":
                return Task.FromResult<string?>(ReadOnly);
            default:
                return Task.FromResult<string?>(NotPossible);
        }

        try
        {
            var startInfo = new ProcessStartInfo(updater) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
            startInfo.ArgumentList.Add("--request");
            startInfo.ArgumentList.Add(handover.RequestPath);
            Process.Start(startInfo)?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            AppLog.UpdaterStartFailed(logger, ex.GetType().Name);
            return Task.FromResult<string?>(NotPossible);
        }

        // Exit code 0 also ends the launcher; the updater waits for both before touching any file.
        lifecycle.RequestShutdown(ExitReason.UserExit);
        return Task.FromResult<string?>(null);
    }
}
