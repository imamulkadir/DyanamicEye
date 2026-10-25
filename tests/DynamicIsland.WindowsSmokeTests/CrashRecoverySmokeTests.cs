using System.Text.Json;

namespace DynamicIsland.WindowsSmokeTests;

[Trait("Category", "WindowsSmoke")]
[Collection(RealAppTests.Name)]
public sealed class CrashRecoverySmokeTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "DynamicIsland.SmokeTests", Guid.NewGuid().ToString("N"));
    private readonly DateTime _testStarted = DateTime.Now;

    public void Dispose()
    {
        RealApp.KillStrayApps(_testStarted);
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Deliberate_exit_through_the_launcher_is_never_relaunched()
    {
        using var launcher = RealApp.Start(RealApp.LauncherExe, IsolatedEnvironment());
        try
        {
            using var app = await RealApp.WaitForAppWindowAsync(_testStarted);

            await RealApp.CloseAndAssertCleanExitAsync(app);
            await RealApp.WaitForExitAsync(launcher, RealApp.ExitTimeout);

            Assert.Equal(0, launcher.ExitCode);
        }
        finally
        {
            RealApp.KillIfRunning(launcher);
        }
    }

#if !DYNAMICISLAND_TEST_HOOKS
    [Fact]
    public async Task Release_build_ignores_the_test_crash_switch()
    {
        var environment = IsolatedEnvironment();
        environment["DYNAMICISLAND_TEST_CRASH"] = "startup";

        using var app = RealApp.Start(RealApp.AppExe, environment);
        try
        {
            await RealApp.WaitForMainWindowAsync(app);
            await RealApp.CloseAndAssertCleanExitAsync(app);
        }
        finally
        {
            RealApp.KillIfRunning(app);
        }

        Assert.False(Directory.Exists(_dataRoot), "Release builds must ignore the test data-root override.");
    }
#else
    [Fact]
    public async Task Injected_ui_crash_exits_with_the_fatal_code_and_keeps_the_session_marker()
    {
        var environment = IsolatedEnvironment();
        environment["DYNAMICISLAND_TEST_CRASH"] = "ui";

        using var app = RealApp.Start(RealApp.AppExe, environment);
        try
        {
            await RealApp.WaitForExitAsync(app, RealApp.WindowTimeout);

            Assert.Equal(1, app.ExitCode);
            Assert.True(File.Exists(Path.Combine(_dataRoot, "data", "session.running")), "A crash must leave the session marked unhealthy.");
            var critical = ReadLogEntries("dynamicisland-").Where(e => e.Level == "Critical").ToArray();
            Assert.Contains(critical, e => e.Event == "UnhandledException");
        }
        finally
        {
            RealApp.KillIfRunning(app);
        }
    }

    [Fact]
    public async Task Crash_loop_restarts_twice_tries_safe_mode_once_then_stops()
    {
        var environment = IsolatedEnvironment();
        environment["DYNAMICISLAND_TEST_CRASH"] = "ui";

        using var launcher = RealApp.Start(RealApp.LauncherExe, environment);
        try
        {
            await RealApp.WaitForExitAsync(launcher, TimeSpan.FromSeconds(120));

            Assert.Equal(1, launcher.ExitCode);
            var runs = ReadLogEntries("launcher-").Where(e => e.Event == "AppRunEnded").Select(e => e.Message).ToArray();
            Assert.Equal(4, runs.Length);
            Assert.EndsWith("action Relaunch", runs[0], StringComparison.Ordinal);
            Assert.EndsWith("action Relaunch", runs[1], StringComparison.Ordinal);
            Assert.EndsWith("action RelaunchInSafeMode", runs[2], StringComparison.Ordinal);
            Assert.Contains("(safe mode True)", runs[3], StringComparison.Ordinal);
            Assert.EndsWith("action Stop", runs[3], StringComparison.Ordinal);
            Assert.Single(ReadLogEntries("dynamicisland-"), e => e.Event == "SafeModeActive");
        }
        finally
        {
            RealApp.KillIfRunning(launcher);
        }
    }
#endif

    /// <summary>Redirects data and logs to a temp folder (honoured by Debug builds only) and suppresses dialogs.</summary>
    private Dictionary<string, string> IsolatedEnvironment() => new()
    {
        ["DYNAMICISLAND_TEST_DATA_ROOT"] = _dataRoot,
        ["DYNAMICISLAND_TEST_NO_DIALOGS"] = "1",
    };

    private LogEntry[] ReadLogEntries(string filePrefix)
    {
        var folder = Path.Combine(_dataRoot, "logs");
        return Directory.Exists(folder)
            ? Directory.GetFiles(folder, filePrefix + "*.log")
                .Order(StringComparer.Ordinal)
                .SelectMany(File.ReadAllLines)
                .Where(l => l.Length > 0)
                .Select(l => JsonSerializer.Deserialize<LogEntry>(l, JsonOptions)!)
                .ToArray()
            : [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record LogEntry(string Level, string? Event, string Message);
}
