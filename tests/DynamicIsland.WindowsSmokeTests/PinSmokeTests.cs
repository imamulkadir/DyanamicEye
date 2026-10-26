#if DYNAMICISLAND_TEST_HOOKS
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Automation;

namespace DynamicIsland.WindowsSmokeTests;

/// <summary>
/// The real app restores the pin saved in settings and saves unpinning (P05-02, ADR 0027). Debug only (isolated data
/// folder). Unpinning goes through the island's UI Automation Toggle pattern, like a screen reader would.
/// </summary>
[Trait("Category", "WindowsSmoke")]
[Collection(RealAppTests.Name)]
public sealed class PinSmokeTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

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
    public async Task Saved_pin_is_restored_and_unpinning_is_saved()
    {
        var settingsPath = Path.Combine(_dataRoot, "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(
            settingsPath,
            """{ "schemaVersion": 1, "general": { "startWithWindows": "Declined" }, "updates": { "automaticChecks": "Declined" }, "behavior": { "pinned": true } }""",
            Ct);

        using var app = RealApp.Start(RealApp.AppExe, new Dictionary<string, string> { ["DYNAMICISLAND_TEST_DATA_ROOT"] = _dataRoot });
        try
        {
            await RealApp.WaitForMainWindowAsync(app, dismissOnboarding: false);
            var island = await WaitForIslandAsync(app.Id, "Dynamic Island, expanded and pinned");
            Assert.NotNull(island);

            ((TogglePattern)island.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
            Assert.NotNull(await WaitForIslandAsync(app.Id, "Dynamic Island, collapsed"));
            Assert.True(await WaitForPinnedSettingAsync(settingsPath, expected: false), "Unpinning must be saved.");

            await RealApp.CloseAndAssertCleanExitAsync(app);
        }
        finally
        {
            RealApp.KillIfRunning(app);
        }
    }

    private static async Task<AutomationElement?> WaitForIslandAsync(int processId, string name)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < Timeout)
        {
            var island = AutomationElement.RootElement.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.ProcessIdProperty, processId),
                    new PropertyCondition(AutomationElement.NameProperty, name)));
            if (island is not null)
            {
                return island;
            }

            await Task.Delay(100, Ct);
        }

        return null;
    }

    private static async Task<bool> WaitForPinnedSettingAsync(string path, bool expected)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < Timeout)
        {
            try
            {
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, Ct));
                if (document.RootElement.GetProperty("behavior").GetProperty("pinned").GetBoolean() == expected)
                {
                    return true;
                }
            }
            catch (IOException)
            {
                // Being replaced right now (atomic save); read again.
            }

            await Task.Delay(100, Ct);
        }

        return false;
    }
}
#endif
