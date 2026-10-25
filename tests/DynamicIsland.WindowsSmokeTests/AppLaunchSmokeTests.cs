using System.Diagnostics;

namespace DynamicIsland.WindowsSmokeTests;

[Trait("Category", "WindowsSmoke")]
public sealed class AppLaunchSmokeTests
{
    private const string AppExecutableName = "DynamicIsland.exe";
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task App_starts_shows_a_window_and_exits_cleanly_when_closed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var exePath = Path.Combine(AppContext.BaseDirectory, AppExecutableName);
        Assert.True(File.Exists(exePath), $"App executable not found: {exePath}");

        using var process = Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Process.Start returned null.");

        try
        {
            var stopwatch = Stopwatch.StartNew();
            while (!HasMainWindow(process) && stopwatch.Elapsed < WindowTimeout)
            {
                Assert.False(process.HasExited, $"App exited during startup with code {SafeExitCode(process)}.");
                await Task.Delay(PollInterval, cancellationToken);
            }

            Assert.True(HasMainWindow(process), $"No main window within {WindowTimeout.TotalSeconds}s.");
            Assert.True(process.CloseMainWindow(), "Close message was not delivered to the main window.");

            using var exitTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            exitTimeout.CancelAfter(ExitTimeout);
            await process.WaitForExitAsync(exitTimeout.Token);

            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private static bool HasMainWindow(Process process)
    {
        process.Refresh();
        return !process.HasExited && process.MainWindowHandle != IntPtr.Zero;
    }

    private static string SafeExitCode(Process process) =>
        process.HasExited ? process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) : "n/a";
}
