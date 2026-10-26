#if DYNAMICISLAND_TEST_HOOKS
using System.Diagnostics;
using System.Windows.Automation;
using DynamicIsland.Platform.Windows.Display;
using DynamicIsland.Platform.Windows.Tray;
using Microsoft.Win32;

namespace DynamicIsland.WindowsSmokeTests;

/// <summary>
/// First run and sign-in startup end to end (P03-04, ADR 0015) with the real app and launcher. Debug only: an isolated
/// data folder and a scratch registry key replace the user's settings and real Run key.
/// </summary>
[Trait("Category", "WindowsSmoke")]
[Collection(RealAppTests.Name)]
public sealed class StartupSmokeTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "DynamicIsland.SmokeTests", Guid.NewGuid().ToString("N"));
    private readonly string _registryRoot = @"Software\DynamicIsland.Tests\" + Guid.NewGuid().ToString("N");
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

        Registry.CurrentUser.DeleteSubKeyTree(_registryRoot, throwOnMissingSubKey: false);
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Sign_in_start_without_permission_waits_then_exits_and_removes_the_stale_entry()
    {
        using (var run = Registry.CurrentUser.CreateSubKey(_registryRoot + @"\Run"))
        {
            run.SetValue("DynamicIsland", "\"C:\\Old\\DynamicIsland.Launcher.exe\" --startup");
        }

        var stopwatch = Stopwatch.StartNew();
        using var launcher = RealApp.Start(RealApp.LauncherExe, Environment(), "--startup");
        try
        {
            await RealApp.WaitForExitAsync(launcher, RealApp.WindowTimeout);
            stopwatch.Stop();

            Assert.Equal(0, launcher.ExitCode);
            Assert.True(stopwatch.Elapsed >= TimeSpan.FromSeconds(4.9), $"The launcher must wait about 5 s at sign-in (took {stopwatch.Elapsed.TotalSeconds:F1} s).");
            Assert.Null(RunValue());
            Assert.Contains("StartupLaunchNotPermitted", AppLog(), StringComparison.Ordinal);
            var launcherLog = ReadLogs("launcher-*.log");
            Assert.Contains("App run 1 ", launcherLog, StringComparison.Ordinal);
            Assert.DoesNotContain("App run 2 ", launcherLog, StringComparison.Ordinal);
        }
        finally
        {
            RealApp.KillIfRunning(launcher);
        }
    }

    [Fact]
    public async Task Onboarding_choice_registers_this_copy_and_a_sign_in_start_runs_quietly()
    {
        if (!Win32TrayIcon.IsShellTrayAvailable)
        {
            Assert.Skip("No shell notification area on this host.");
        }

        using (var app = RealApp.Start(RealApp.AppExe, Environment()))
        {
            try
            {
                await RealApp.WaitForMainWindowAsync(app, dismissOnboarding: false);
                Invoke(app.Id, "Onboarding.Startup", toggle: true);
                Invoke(app.Id, "Onboarding.Continue");

                var expected = $"\"{Path.Combine(AppContext.BaseDirectory, RealApp.LauncherExe)}\" --startup";
                Assert.True(await WaitUntilAsync(() => string.Equals(RunValue(), expected, StringComparison.OrdinalIgnoreCase)), $"Run value was '{RunValue()}'.");
                Assert.True(await WaitUntilAsync(() => IslandWindowInterop.FindTopLevelWindow(app.Id, RealApp.OnboardingTitle) == IntPtr.Zero), "Continue must close onboarding.");

                await TrayMenuDriver.InvokeAsync(app, "Tray.Exit");
                await RealApp.WaitForExitAsync(app, RealApp.ExitTimeout);
                Assert.Equal(0, app.ExitCode);
            }
            finally
            {
                RealApp.KillIfRunning(app);
            }
        }

        // Now a sign-in start: delayed, no onboarding, island shown.
        var started = DateTime.Now;
        using var launcher = RealApp.Start(RealApp.LauncherExe, Environment(), "--startup");
        try
        {
            using var second = await RealApp.WaitForAppWindowAsync(started);
            Assert.True(second.StartTime - started >= TimeSpan.FromSeconds(4.5), "The app must start about 5 s after sign-in.");
            await Task.Delay(1000, TestContext.Current.CancellationToken);
            Assert.Equal(IntPtr.Zero, IslandWindowInterop.FindTopLevelWindow(second.Id, RealApp.OnboardingTitle));

            await TrayMenuDriver.InvokeAsync(second, "Tray.Exit");
            await RealApp.WaitForExitAsync(launcher, RealApp.ExitTimeout);
            Assert.Equal(0, launcher.ExitCode);
            Assert.NotNull(RunValue());
        }
        finally
        {
            RealApp.KillIfRunning(launcher);
        }
    }

    private Dictionary<string, string> Environment() => new()
    {
        ["DYNAMICISLAND_TEST_DATA_ROOT"] = _dataRoot,
        ["DYNAMICISLAND_TEST_REGISTRY_ROOT"] = _registryRoot,
        ["DYNAMICISLAND_TEST_NO_DIALOGS"] = "1",
    };

    private string? RunValue()
    {
        using var run = Registry.CurrentUser.OpenSubKey(_registryRoot + @"\Run");
        return run?.GetValue("DynamicIsland") as string;
    }

    private string AppLog() => ReadLogs("dynamicisland-*.log");

    private string ReadLogs(string pattern)
    {
        var folder = Path.Combine(_dataRoot, "logs");
        if (!Directory.Exists(folder))
        {
            return string.Empty;
        }

        return string.Join('\n', Directory.GetFiles(folder, pattern).Select(f =>
        {
            using var stream = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }));
    }

    /// <summary>Clicks or toggles a control of the first-run window through UI Automation (no synthetic mouse).</summary>
    private static void Invoke(int processId, string automationId, bool toggle = false)
    {
        var stopwatch = Stopwatch.StartNew();
        AutomationElement? element = null;
        while (element is null && stopwatch.Elapsed < Timeout)
        {
            element = AutomationElement.RootElement.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.ProcessIdProperty, processId),
                    new PropertyCondition(AutomationElement.AutomationIdProperty, automationId)));
            if (element is null)
            {
                Thread.Sleep(100);
            }
        }

        Assert.NotNull(element);
        if (toggle)
        {
            ((TogglePattern)element.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
        }
        else
        {
            ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
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

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return true;
    }
}
#endif
