#if DYNAMICISLAND_TEST_HOOKS
using System.Diagnostics;
using DynamicIsland.Core.Distribution;
using Microsoft.Win32;

namespace DynamicIsland.WindowsSmokeTests;

/// <summary>
/// The release layout (P06-01, ADR 0030) built from the test binaries: a root launcher named DynamicIsland.exe,
/// <c>current.json</c> and versioned payload folders. Debug only (isolated data folder and registry key). Release
/// packages use a NativeAOT launcher; the layout rules are the same.
/// </summary>
[Trait("Category", "WindowsSmoke")]
[Collection(RealAppTests.Name)]
public sealed class LayoutSmokeTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "DynamicIsland.SmokeTests", Guid.NewGuid().ToString("N"));
    private readonly string _registryRoot = @"Software\DynamicIsland.Tests\" + Guid.NewGuid().ToString("N");
    private readonly DateTime _testStarted = DateTime.Now;

    private string Layout => Path.Combine(_root, "layout");

    private string DataRoot => Path.Combine(_root, "data-root");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        RealApp.KillStrayApps(_testStarted);
        Registry.CurrentUser.DeleteSubKeyTree(_registryRoot, throwOnMissingSubKey: false);
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Root_launcher_runs_the_current_payload_and_registers_itself_for_sign_in()
    {
        BuildLayout("0.1.0");
        WritePointer("""{ "current": "0.1.0", "previous": null }""");
        await SeedSettingsAsync(startWithWindows: true);

        var appPath = await RunLayoutAsync();

        Assert.Equal(Path.Combine(Layout, "app-0.1.0", AppLayout.AppExecutable), appPath, ignoreCase: true);
        // The startup entry points at the stable root launcher, not into a version folder that updates replace.
        Assert.Equal($"\"{Path.Combine(Layout, AppLayout.RootLauncher)}\" --startup", RunValue(), ignoreCase: true);
    }

    [Fact]
    public async Task A_missing_current_payload_falls_back_to_the_previous_one()
    {
        BuildLayout("0.1.0");
        WritePointer("""{ "current": "0.2.0", "previous": "0.1.0" }""");
        await SeedSettingsAsync(startWithWindows: false);

        var appPath = await RunLayoutAsync();

        Assert.Equal(Path.Combine(Layout, "app-0.1.0", AppLayout.AppExecutable), appPath, ignoreCase: true);
        var launcherLog = string.Join('\n', Directory.GetFiles(Path.Combine(DataRoot, "logs"), "launcher-*.log").Select(File.ReadAllText));
        Assert.Contains("UsingPreviousVersion", launcherLog, StringComparison.Ordinal);
    }

    /// <summary>Starts the root launcher, waits for the app window, closes it and checks both exit cleanly.</summary>
    /// <returns>The app's executable path.</returns>
    private async Task<string> RunLayoutAsync()
    {
        var startInfo = new ProcessStartInfo(Path.Combine(Layout, AppLayout.RootLauncher)) { UseShellExecute = false, WorkingDirectory = Layout };
        startInfo.Environment["DYNAMICISLAND_TEST_DATA_ROOT"] = DataRoot;
        startInfo.Environment["DYNAMICISLAND_TEST_REGISTRY_ROOT"] = _registryRoot;
        startInfo.Environment["DYNAMICISLAND_TEST_NO_DIALOGS"] = "1";
        var started = DateTime.Now;
        using var launcher = Process.Start(startInfo)!;
        try
        {
            using var app = await WaitForAppAsync(launcher.Id, started);
            var path = app.MainModule!.FileName;
            await RealApp.CloseAndAssertCleanExitAsync(app);
            await RealApp.WaitForExitAsync(launcher, RealApp.ExitTimeout);
            Assert.Equal(0, launcher.ExitCode);
            return path;
        }
        finally
        {
            RealApp.KillIfRunning(launcher);
        }
    }

    /// <summary>The app process (same image name as the root launcher) once it shows its window.</summary>
    private static async Task<Process> WaitForAppAsync(int launcherId, DateTime startedAfter)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < Timeout)
        {
            foreach (var candidate in Process.GetProcessesByName(RealApp.AppProcessName))
            {
                candidate.Refresh();
                if (candidate.Id != launcherId && candidate.StartTime >= startedAfter && candidate.MainWindowHandle != IntPtr.Zero)
                {
                    return candidate;
                }

                candidate.Dispose();
            }

            await Task.Delay(200, Ct);
        }

        throw new TimeoutException("The app did not show a window.");
    }

    /// <summary>
    /// Root: everything the launcher needs, with DynamicIsland.Launcher.exe copied to DynamicIsland.exe (its apphost
    /// still loads DynamicIsland.Launcher.dll). Payload: the app files in app-&lt;version&gt;.
    /// </summary>
    private void BuildLayout(string version)
    {
        CopyTree(AppContext.BaseDirectory, Layout);
        File.Copy(Path.Combine(AppContext.BaseDirectory, RealApp.LauncherExe), Path.Combine(Layout, AppLayout.RootLauncher), overwrite: true);
        CopyTree(AppContext.BaseDirectory, AppLayout.PayloadFolder(Layout, version));
    }

    private void WritePointer(string json) => File.WriteAllText(Path.Combine(Layout, AppLayout.PointerFileName), json);

    private async Task SeedSettingsAsync(bool startWithWindows)
    {
        var data = Path.Combine(DataRoot, "data");
        Directory.CreateDirectory(data);
        var consent = startWithWindows ? "Granted" : "Declined";
        await File.WriteAllTextAsync(
            Path.Combine(data, "settings.json"),
            $$"""{ "schemaVersion": 1, "general": { "startWithWindows": "{{consent}}" }, "updates": { "automaticChecks": "Declined" } }""",
            Ct);
    }

    private string? RunValue()
    {
        using var run = Registry.CurrentUser.OpenSubKey(_registryRoot + @"\Run");
        return run?.GetValue("DynamicIsland") as string;
    }

    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
        }
    }
}
#endif
