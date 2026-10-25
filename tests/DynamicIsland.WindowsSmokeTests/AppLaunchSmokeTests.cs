using System.Diagnostics;

namespace DynamicIsland.WindowsSmokeTests;

/// <summary>Drives the real <c>DynamicIsland.exe</c>. Tests in this class run sequentially (one app per user at a time).</summary>
[Trait("Category", "WindowsSmoke")]
public sealed class AppLaunchSmokeTests
{
    private const string AppExecutableName = "DynamicIsland.exe";
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task App_starts_shows_a_window_and_exits_cleanly_when_closed()
    {
        using var app = StartApp();
        try
        {
            await WaitForMainWindowAsync(app);

            await CloseAndAssertCleanExitAsync(app);
        }
        finally
        {
            KillIfRunning(app);
        }
    }

    [Fact]
    public async Task Second_launch_exits_quietly_and_leaves_the_first_instance_running()
    {
        using var first = StartApp();
        try
        {
            await WaitForMainWindowAsync(first);

            using var second = StartApp();
            try
            {
                await WaitForExitAsync(second);
                Assert.Equal(0, second.ExitCode);
                Assert.Equal(IntPtr.Zero, second.MainWindowHandle);
            }
            finally
            {
                KillIfRunning(second);
            }

            Assert.False(first.HasExited, "The first instance must keep running.");
            await CloseAndAssertCleanExitAsync(first);
        }
        finally
        {
            KillIfRunning(first);
        }
    }

    private static Process StartApp()
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, AppExecutableName);
        Assert.True(File.Exists(exePath), $"App executable not found: {exePath}");
        return Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Process.Start returned null.");
    }

    private static async Task WaitForMainWindowAsync(Process process)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!HasMainWindow(process) && stopwatch.Elapsed < WindowTimeout)
        {
            Assert.False(process.HasExited, $"App exited during startup with code {SafeExitCode(process)}.");
            await Task.Delay(PollInterval, Ct);
        }

        Assert.True(HasMainWindow(process), $"No main window within {WindowTimeout.TotalSeconds}s.");
    }

    private static async Task CloseAndAssertCleanExitAsync(Process process)
    {
        Assert.True(process.CloseMainWindow(), "Close message was not delivered to the main window.");
        await WaitForExitAsync(process);
        Assert.Equal(0, process.ExitCode);
    }

    private static async Task WaitForExitAsync(Process process)
    {
        using var exitTimeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        exitTimeout.CancelAfter(ExitTimeout);
        await process.WaitForExitAsync(exitTimeout.Token);
    }

    private static bool HasMainWindow(Process process)
    {
        process.Refresh();
        return !process.HasExited && process.MainWindowHandle != IntPtr.Zero;
    }

    private static void KillIfRunning(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }

    private static string SafeExitCode(Process process) =>
        process.HasExited ? process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) : "n/a";
}
