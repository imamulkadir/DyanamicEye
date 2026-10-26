using System.Diagnostics;
using System.Globalization;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Platform.Windows.Display;

namespace DynamicIsland.WindowsSmokeTests;

/// <summary>All tests that start the real app share the per-user single-instance mutex, so they never run in parallel.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RealAppTests
{
    public const string Name = "RealApp";
}

/// <summary>Helpers for driving <c>DynamicIsland.exe</c> and <c>DynamicIsland.Launcher.exe</c> from the test output folder.</summary>
internal static class RealApp
{
    public const string AppExe = "DynamicIsland.exe";
    public const string LauncherExe = "DynamicIsland.Launcher.exe";
    public const string AppProcessName = "DynamicIsland";
    public const string OnboardingTitle = "Welcome to Dynamic Island";

    /// <summary>
    /// Scratch key standing for <c>Software\Microsoft\Windows\CurrentVersion</c> in Debug runs, so the app's startup
    /// check never touches the real Run key (ADR 0015 §7). Release builds ignore the variable.
    /// </summary>
    public const string ScratchRegistryRoot = @"Software\DynamicIsland.Tests\Smoke";

    private static readonly TimeSpan OnboardingWait = TimeSpan.FromSeconds(1.5);

    public static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static Process Start(string exeName, IReadOnlyDictionary<string, string>? environment = null, params string[] arguments)
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, exeName);
        Assert.True(File.Exists(exePath), $"Executable not found: {exePath}");

        var startInfo = new ProcessStartInfo(exePath) { UseShellExecute = false };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment[TestHooks.RegistryRootVariable] = ScratchRegistryRoot;

        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[name] = value;
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Process.Start returned null.");
    }

    /// <param name="dismissOnboarding">Close the first-run window (without saving) if it appears, as most tests don't need it.</param>
    public static async Task WaitForMainWindowAsync(Process process, bool dismissOnboarding = true)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!HasMainWindow(process) && stopwatch.Elapsed < WindowTimeout)
        {
            Assert.False(process.HasExited, $"Process exited during startup with code {SafeExitCode(process)}.");
            await Task.Delay(PollInterval, Ct);
        }

        Assert.True(HasMainWindow(process), $"No main window within {WindowTimeout.TotalSeconds}s.");
        if (dismissOnboarding)
        {
            await DismissOnboardingAsync(process.Id);
        }
    }

    /// <summary>Closes the first-run window if it shows up shortly after the island (closing saves nothing).</summary>
    public static async Task DismissOnboardingAsync(int processId)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < OnboardingWait)
        {
            var onboarding = IslandWindowInterop.FindTopLevelWindow(processId, OnboardingTitle);
            if (onboarding != IntPtr.Zero)
            {
                IslandWindowInterop.RequestClose(onboarding);
                while (IslandWindowInterop.FindTopLevelWindow(processId, OnboardingTitle) != IntPtr.Zero && stopwatch.Elapsed < WindowTimeout)
                {
                    await Task.Delay(50, Ct);
                }

                return;
            }

            await Task.Delay(50, Ct);
        }
    }

    /// <summary>Finds the app process (started by the launcher) once it shows a window.</summary>
    public static async Task<Process> WaitForAppWindowAsync(DateTime startedAfter)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < WindowTimeout)
        {
            foreach (var candidate in Process.GetProcessesByName(AppProcessName))
            {
                if (candidate.StartTime >= startedAfter && HasMainWindow(candidate))
                {
                    await DismissOnboardingAsync(candidate.Id);
                    return candidate;
                }

                candidate.Dispose();
            }

            await Task.Delay(PollInterval, Ct);
        }

        throw new TimeoutException($"No {AppProcessName} window within {WindowTimeout.TotalSeconds}s.");
    }

    public static async Task CloseAndAssertCleanExitAsync(Process process)
    {
        Assert.True(process.CloseMainWindow(), "Close message was not delivered to the main window.");
        await WaitForExitAsync(process, ExitTimeout);
        Assert.Equal(0, process.ExitCode);
    }

    public static async Task WaitForExitAsync(Process process, TimeSpan timeout)
    {
        using var exitTimeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        exitTimeout.CancelAfter(timeout);
        await process.WaitForExitAsync(exitTimeout.Token);
    }

    public static void KillIfRunning(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }

    /// <summary>Kills app processes a failed test may have left behind (only those started after <paramref name="startedAfter"/>).</summary>
    public static void KillStrayApps(DateTime startedAfter)
    {
        foreach (var process in Process.GetProcessesByName(AppProcessName))
        {
            using (process)
            {
                if (!process.HasExited && process.StartTime >= startedAfter)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
        }
    }

    private static bool HasMainWindow(Process process)
    {
        process.Refresh();
        return !process.HasExited && process.MainWindowHandle != IntPtr.Zero;
    }

    private static string SafeExitCode(Process process) =>
        process.HasExited ? process.ExitCode.ToString(CultureInfo.InvariantCulture) : "n/a";
}
