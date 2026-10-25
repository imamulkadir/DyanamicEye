using System.Diagnostics;
using DynamicIsland.Platform.Windows.Display;
using DynamicIsland.Platform.Windows.Tray;

namespace DynamicIsland.WindowsSmokeTests;

/// <summary>The real app shows its notification-area icon, recovers it after an Explorer restart and removes it on exit.</summary>
[Trait("Category", "WindowsSmoke")]
[Collection(RealAppTests.Name)]
public sealed class TraySmokeTests
{
    private const string TrayWindowTitle = "DynamicIsland.Tray";
    private const uint TrayIconId = 1;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task App_shows_a_tray_icon_recovers_it_after_explorer_restart_and_removes_it_on_exit()
    {
        if (!Win32TrayIcon.IsShellTrayAvailable)
        {
            Assert.Skip("No shell notification area on this host.");
        }

        using var app = RealApp.Start(RealApp.AppExe);
        try
        {
            await RealApp.WaitForMainWindowAsync(app);
            var owner = IntPtr.Zero;
            Assert.True(await WaitUntilAsync(() => (owner = IslandWindowInterop.FindTopLevelWindow(app.Id, TrayWindowTitle)) != IntPtr.Zero), "Tray owner window not found.");
            Assert.True(await WaitUntilAsync(() => Win32TrayIcon.IsRegistered(owner, TrayIconId)), "Tray icon not registered.");

            // Simulated Explorer restart: the app must re-add its icon when told the taskbar was recreated.
            Win32TrayIcon.SendTaskbarCreated(owner);
            Assert.True(Win32TrayIcon.IsRegistered(owner, TrayIconId), "Tray icon missing after TaskbarCreated.");

            await RealApp.CloseAndAssertCleanExitAsync(app);
            Assert.False(Win32TrayIcon.IsRegistered(owner, TrayIconId), "Tray icon must be removed on exit.");
        }
        finally
        {
            RealApp.KillIfRunning(app);
        }
    }

    /// <summary>Regression: taskkill (no /F) also closes the hidden tray window; that must be a clean Exit, not a broken tray.</summary>
    [Fact]
    public async Task Close_request_to_the_tray_window_exits_the_app_cleanly()
    {
        using var app = RealApp.Start(RealApp.AppExe);
        try
        {
            await RealApp.WaitForMainWindowAsync(app);
            var owner = IntPtr.Zero;
            Assert.True(await WaitUntilAsync(() => (owner = IslandWindowInterop.FindTopLevelWindow(app.Id, TrayWindowTitle)) != IntPtr.Zero), "Tray owner window not found.");

            IslandWindowInterop.RequestClose(owner);

            await RealApp.WaitForExitAsync(app, RealApp.ExitTimeout);
            Assert.Equal(0, app.ExitCode);
        }
        finally
        {
            RealApp.KillIfRunning(app);
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > Timeout)
            {
                return false;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        return true;
    }
}
