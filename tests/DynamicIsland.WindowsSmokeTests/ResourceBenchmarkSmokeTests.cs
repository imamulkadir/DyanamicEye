#if DYNAMICISLAND_TEST_HOOKS
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows.Automation;
using DynamicIsland.Platform.Windows.Tray;

namespace DynamicIsland.WindowsSmokeTests;

/// <summary>
/// P05-04 / G05 benchmark: the real app with every widget on, measured in four states (collapsed, open on the clock
/// tab, open on the system stats tab, paused). Reports CPU (share of one core and of the whole PC) and private memory
/// for each, writes them to <c>benchmark-results.json</c> next to the tests, and fails only on runaway values; the G05
/// targets (idle CPU ≤ 0.5 %, private memory ≤ 100 MB) are goals recorded in docs/PERFORMANCE.md. Debug only (isolated
/// data folder).
/// </summary>
[Trait("Category", "WindowsSmoke")]
[Collection(RealAppTests.Name)]
public sealed class ResourceBenchmarkSmokeTests : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Runaway guards (share of one core), generous for 2-vCPU CI runners: far above the goals, far below a busy loop.</summary>
    private const double MaxIdleCoreShare = 0.02;
    private const double MaxStatsCoreShare = 0.15;

    /// <summary>Private memory may grow this much over 40 open/close cycles with tab switches (no obvious UI leaks, G05).</summary>
    private const long MaxCycleGrowthMegabytes = 40;
    private const int Cycles = 40;
    private const long MaxPrivateBytes = 250L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "DynamicIsland.SmokeTests", Guid.NewGuid().ToString("N"));
    private readonly DateTime _testStarted = DateTime.Now;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        RealApp.KillStrayApps(_testStarted);
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Collapsed_open_and_paused_states_stay_within_the_resource_budget()
    {
        if (!Win32TrayIcon.IsShellTrayAvailable)
        {
            Assert.Skip("No shell notification area on this host (pausing goes through the tray).");
        }

        var settingsPath = Path.Combine(_dataRoot, "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(
            settingsPath,
            """{ "schemaVersion": 1, "general": { "startWithWindows": "Declined" }, "updates": { "automaticChecks": "Declined" } }""",
            Ct);

        using var app = RealApp.Start(RealApp.AppExe, new Dictionary<string, string> { ["DYNAMICISLAND_TEST_DATA_ROOT"] = _dataRoot });
        var results = new List<Measurement>();
        try
        {
            await RealApp.WaitForMainWindowAsync(app, dismissOnboarding: false);
            var island = await FindAsync(app.Id, new PropertyCondition(AutomationElement.AutomationIdProperty, "IslandSurface"));

            results.Add(await MeasureAsync(app, "collapsed"));

            ((ExpandCollapsePattern)island.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
            await SelectTabAsync(app.Id, "Tab.clock");
            results.Add(await MeasureAsync(app, "open, clock tab"));

            await SelectTabAsync(app.Id, "Tab.system");
            results.Add(await MeasureAsync(app, "open, system stats tab"));

            var expandCollapse = (ExpandCollapsePattern)island.GetCurrentPattern(ExpandCollapsePattern.Pattern);
            for (var i = 0; i < Cycles; i++)
            {
                expandCollapse.Collapse();
                expandCollapse.Expand();
                await SelectTabAsync(app.Id, i % 2 == 0 ? "Tab.volume" : "Tab.tasks");
            }

            await SelectTabAsync(app.Id, "Tab.clock");
            results.Add(await MeasureAsync(app, $"open, clock tab, after {Cycles} open/close cycles"));

            await TrayMenuDriver.InvokeAsync(app, "Tray.TogglePause");
            results.Add(await MeasureAsync(app, "paused"));

            await TrayMenuDriver.InvokeAsync(app, "Tray.Exit");
            await RealApp.WaitForExitAsync(app, RealApp.ExitTimeout);
            Assert.Equal(0, app.ExitCode);
        }
        finally
        {
            RealApp.KillIfRunning(app);
        }

        var report = JsonSerializer.Serialize(
            new { measuredUtc = DateTime.UtcNow, Environment.ProcessorCount, windowSeconds = Window.TotalSeconds, results },
            JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "benchmark-results.json"), report, Ct);
        TestContext.Current.SendDiagnosticMessage(report);
        TestContext.Current.TestOutputHelper?.WriteLine(report);

        var beforeCycles = results.Single(r => r.State == "open, system stats tab").PrivateMegabytes;
        var afterCycles = results.Single(r => r.State.Contains("cycles", StringComparison.Ordinal)).PrivateMegabytes;
        Assert.True(afterCycles - beforeCycles < MaxCycleGrowthMegabytes, $"Private memory grew {afterCycles - beforeCycles} MB over {Cycles} open/close cycles.");

        Assert.All(results, r =>
        {
            var limit = r.State.Contains("stats", StringComparison.Ordinal) ? MaxStatsCoreShare : MaxIdleCoreShare;
            Assert.True(r.CpuOneCorePercent / 100 < limit, $"{r.State}: {r.CpuOneCorePercent.ToString("F2", CultureInfo.InvariantCulture)} % of one core (runaway limit {limit:P0}).");
            Assert.True(r.PrivateMegabytes * 1024 * 1024 < MaxPrivateBytes, $"{r.State}: {r.PrivateMegabytes} MB private memory.");
        });
    }

    private static async Task<Measurement> MeasureAsync(Process app, string state)
    {
        await Task.Delay(Settle, Ct);
        app.Refresh();
        var cpuBefore = app.TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        await Task.Delay(Window, Ct);
        app.Refresh();
        var cpu = (app.TotalProcessorTime - cpuBefore).TotalMilliseconds;
        var oneCore = 100 * cpu / wall.Elapsed.TotalMilliseconds;
        return new Measurement(
            state,
            Math.Round(cpu),
            Math.Round(oneCore, 3),
            Math.Round(oneCore / Environment.ProcessorCount, 3),
            app.PrivateMemorySize64 / (1024 * 1024),
            app.WorkingSet64 / (1024 * 1024));
    }

    private static async Task SelectTabAsync(int processId, string automationId)
    {
        var tab = await FindAsync(processId, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
        ((SelectionItemPattern)tab.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
    }

    private static async Task<AutomationElement> FindAsync(int processId, Condition condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var element = AutomationElement.RootElement.FindFirst(
                TreeScope.Descendants,
                new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, processId), condition));
            if (element is not null)
            {
                return element;
            }

            Assert.True(stopwatch.Elapsed < Timeout, "UI element not found.");
            await Task.Delay(100, Ct);
        }
    }

    /// <param name="CpuOneCorePercent">CPU time over wall time, in percent of one core.</param>
    /// <param name="CpuMachinePercent">The same over all logical processors (what Task Manager shows).</param>
    private sealed record Measurement(string State, double CpuMilliseconds, double CpuOneCorePercent, double CpuMachinePercent, long PrivateMegabytes, long WorkingSetMegabytes);
}
#endif
