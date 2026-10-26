using System.Diagnostics;
using DynamicIsland.Platform.Windows.Display;
using DynamicIsland.Platform.Windows.Tray;

namespace DynamicIsland.WindowsSmokeTests;

/// <summary>Tray actions on the real app (P03-02). Tests that write settings or restart run only in Debug (isolated data root).</summary>
[Trait("Category", "WindowsSmoke")]
[Collection(RealAppTests.Name)]
public sealed class TrayActionSmokeTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "DynamicIsland.SmokeTests", Guid.NewGuid().ToString("N"));
    private readonly DateTime _testStarted = DateTime.Now;

    public void Dispose()
    {
        RealApp.KillStrayApps(_testStarted);
        foreach (var launcher in Process.GetProcessesByName("DynamicIsland.Launcher"))
        {
            using (launcher)
            {
                if (launcher.StartTime >= _testStarted)
                {
                    launcher.Kill();
                }
            }
        }

        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Pause_hides_the_island_and_resume_brings_it_back()
    {
        SkipWithoutTray();
        using var app = RealApp.Start(RealApp.AppExe, Isolated());
        try
        {
            await RealApp.WaitForMainWindowAsync(app);
            var island = app.MainWindowHandle;

            await TrayMenuDriver.InvokeAsync(app, "Tray.TogglePause");
            Assert.True(await WaitUntilAsync(() => !IslandWindowInterop.IsVisible(island)), "Pause must hide the island.");

            await TrayMenuDriver.InvokeAsync(app, "Tray.TogglePause");
            Assert.True(await WaitUntilAsync(() => IslandWindowInterop.IsVisible(island)), "Resume must show the island again.");

            await TrayMenuDriver.InvokeAsync(app, "Tray.Exit");
            await RealApp.WaitForExitAsync(app, RealApp.ExitTimeout);
            Assert.Equal(0, app.ExitCode);
        }
        finally
        {
            RealApp.KillIfRunning(app);
        }
    }

    [Fact]
    public async Task Settings_opens_once_and_closing_it_keeps_the_app_running()
    {
        SkipWithoutTray();
        using var app = RealApp.Start(RealApp.AppExe, Isolated());
        try
        {
            await RealApp.WaitForMainWindowAsync(app);
            var island = app.MainWindowHandle;

            await TrayMenuDriver.InvokeAsync(app, "Tray.Settings");
            var settings = await WaitForTopLevelAsync(app.Id, "Dynamic Island settings");

            // A second request activates the open window instead of opening another one.
            await TrayMenuDriver.InvokeAsync(app, "Tray.Settings");
            await Task.Delay(500, TestContext.Current.CancellationToken);
            Assert.Equal(settings, IslandWindowInterop.FindTopLevelWindow(app.Id, "Dynamic Island settings"));

            IslandWindowInterop.RequestClose(settings);
            Assert.True(await WaitUntilAsync(() => IslandWindowInterop.FindTopLevelWindow(app.Id, "Dynamic Island settings") == IntPtr.Zero), "Settings window must close.");
            await Task.Delay(1000, TestContext.Current.CancellationToken);
            Assert.False(app.HasExited, "Closing settings must not exit the app.");
            Assert.True(IslandWindowInterop.IsVisible(island));

            await TrayMenuDriver.InvokeAsync(app, "Tray.Exit");
            await RealApp.WaitForExitAsync(app, RealApp.ExitTimeout);
            Assert.Equal(0, app.ExitCode);
        }
        finally
        {
            RealApp.KillIfRunning(app);
        }
    }

#if DYNAMICISLAND_TEST_HOOKS
    [Fact]
    public async Task Reset_display_saves_the_primary_display_choice()
    {
        SkipWithoutTray();
        using var app = RealApp.Start(RealApp.AppExe, Isolated());
        try
        {
            await RealApp.WaitForMainWindowAsync(app);

            await TrayMenuDriver.InvokeAsync(app, "Tray.Display", "Tray.ResetDisplay");

            var settingsPath = Path.Combine(_dataRoot, "data", "settings.json");
            Assert.True(await WaitUntilAsync(() => File.Exists(settingsPath)), "Reset must save settings.");
            using var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath, TestContext.Current.CancellationToken));
            var general = json.RootElement.GetProperty("general");
            Assert.True(!general.TryGetProperty("preferredMonitorId", out var id) || id.ValueKind == System.Text.Json.JsonValueKind.Null);
            Assert.Equal(0, json.RootElement.GetProperty("appearance").GetProperty("verticalOffset").GetDouble());

            await RealApp.CloseAndAssertCleanExitAsync(app);
        }
        finally
        {
            RealApp.KillIfRunning(app);
        }
    }

    [Fact]
    public async Task Restart_through_the_launcher_starts_a_new_instance_and_safe_mode_restart_uses_safe_mode()
    {
        SkipWithoutTray();
        using var launcher = RealApp.Start(RealApp.LauncherExe, Isolated());
        try
        {
            using var first = await RealApp.WaitForAppWindowAsync(_testStarted);

            await TrayMenuDriver.InvokeAsync(first, "Tray.Restart");
            await RealApp.WaitForExitAsync(first, RealApp.ExitTimeout);
            Assert.Equal(3, first.ExitCode);

            using var second = await WaitForNewAppAsync(first.Id);
            await TrayMenuDriver.InvokeAsync(second, "Tray.ToggleSafeMode");
            await RealApp.WaitForExitAsync(second, RealApp.ExitTimeout);
            Assert.Equal(4, second.ExitCode);

            using var third = await WaitForNewAppAsync(second.Id);
            Assert.True(await WaitUntilAsync(() => AppLog().Contains("SafeModeActive", StringComparison.Ordinal)), "Third instance must run in Safe Mode.");

            await TrayMenuDriver.InvokeAsync(third, "Tray.Exit");
            await RealApp.WaitForExitAsync(launcher, RealApp.ExitTimeout);
            Assert.Equal(0, launcher.ExitCode);
        }
        finally
        {
            RealApp.KillIfRunning(launcher);
        }
    }

    [Fact]
    public async Task Restart_without_the_launcher_hands_over_to_the_launcher()
    {
        SkipWithoutTray();
        using var app = RealApp.Start(RealApp.AppExe, Isolated());
        try
        {
            await RealApp.WaitForMainWindowAsync(app);

            await TrayMenuDriver.InvokeAsync(app, "Tray.Restart");
            await RealApp.WaitForExitAsync(app, RealApp.ExitTimeout);

            using var next = await WaitForNewAppAsync(app.Id);
            await TrayMenuDriver.InvokeAsync(next, "Tray.Exit");
            await RealApp.WaitForExitAsync(next, RealApp.ExitTimeout);
            Assert.Equal(0, next.ExitCode);
        }
        finally
        {
            RealApp.KillIfRunning(app);
        }
    }
#endif

    private static void SkipWithoutTray()
    {
        if (!Win32TrayIcon.IsShellTrayAvailable)
        {
            Assert.Skip("No shell notification area on this host.");
        }
    }

    private Dictionary<string, string> Isolated() => new()
    {
        ["DYNAMICISLAND_TEST_DATA_ROOT"] = _dataRoot,
        ["DYNAMICISLAND_TEST_NO_DIALOGS"] = "1",
    };

    private async Task<Process> WaitForNewAppAsync(int previousId)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < Timeout)
        {
            var candidate = await RealApp.WaitForAppWindowAsync(_testStarted);
            if (candidate.Id != previousId)
            {
                return candidate;
            }

            candidate.Dispose();
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("No new app instance appeared.");
    }

    private string AppLog()
    {
        var folder = Path.Combine(_dataRoot, "logs");
        if (!Directory.Exists(folder))
        {
            return string.Empty;
        }

        return string.Join('\n', Directory.GetFiles(folder, "dynamicisland-*.log").Select(f =>
        {
            using var stream = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }));
    }

    private static async Task<IntPtr> WaitForTopLevelAsync(int processId, string title)
    {
        var handle = IntPtr.Zero;
        Assert.True(await WaitUntilAsync(() => (handle = IslandWindowInterop.FindTopLevelWindow(processId, title)) != IntPtr.Zero), $"Window '{title}' did not appear.");
        return handle;
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

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return true;
    }
}
