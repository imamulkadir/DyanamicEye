using System.Diagnostics;
using DynamicIsland.Infrastructure.Updates;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DynamicIsland.Updater;

/// <summary>Real process control for the updater (ADR 0035).</summary>
internal sealed class WindowsUpdaterProcesses : IUpdaterProcesses
{
    public async Task<bool> WaitForExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return true; // Already gone.
        }

        using (process)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }
    }

    public int StartLauncher(string launcherPath)
    {
        var startInfo = new ProcessStartInfo(launcherPath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(launcherPath)! };
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The launcher could not be started.");
        return process.Id;
    }

    public void StopTree(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone or not ours to stop.
        }
    }
}

/// <summary>
/// Installed copies only: keeps "Installed apps" showing the running version by updating <c>DisplayVersion</c> in the
/// per-user uninstall key written by the installer (HKCU, no elevation). Portable copies have no key and are skipped.
/// </summary>
internal sealed class UninstallEntryVersion : IInstalledVersionRecorder
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F2E6B1C-3A47-4C2D-9B6E-5D1F0A7C4E21}_is1";

    public void Record(string layoutRoot, string version)
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey, writable: true);
        if (key?.GetValue("InstallLocation") is string location
            && string.Equals(Path.GetFullPath(location).TrimEnd('\\'), Path.GetFullPath(layoutRoot).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            key.SetValue("DisplayVersion", version);
            key.SetValue("DisplayName", $"Dynamic Island {version}");
        }
    }
}

internal static partial class UpdaterLog
{
    [LoggerMessage(EventId = 3004, Level = LogLevel.Warning, Message = "Updater started with an invalid or missing request; nothing was changed")]
    public static partial void InvalidArguments(ILogger logger);
}
