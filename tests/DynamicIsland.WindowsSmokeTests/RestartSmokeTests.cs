#if DYNAMICISLAND_TEST_HOOKS
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DynamicIsland.Infrastructure.PersonalData;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicIsland.WindowsSmokeTests;

/// <summary>
/// G04: the real app survives 100 start/exit cycles on one data folder without losing or corrupting anything, and no
/// widget ever fails. Debug only: isolated data folder.
/// </summary>
[Trait("Category", "WindowsSmoke")]
[Collection(RealAppTests.Name)]
public sealed class RestartSmokeTests : IDisposable
{
    private const int Restarts = 100;

    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "DynamicIsland.SmokeTests", Guid.NewGuid().ToString("N"));
    private readonly DateTime _testStarted = DateTime.Now;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        RealApp.KillStrayApps(_testStarted);
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Hundred_restarts_keep_data_settings_and_widgets_healthy()
    {
        var data = Path.Combine(_dataRoot, "data");
        Directory.CreateDirectory(data);
        var settingsPath = Path.Combine(data, "settings.json");
        // Both consents answered, so the first-run window never appears.
        await File.WriteAllTextAsync(
            settingsPath,
            """{ "schemaVersion": 1, "general": { "startWithWindows": "Declined" }, "updates": { "automaticChecks": "Declined" } }""",
            Ct);
        await SeedAsync(data);
        var expectedData = await ReadDataAsync(data);
        byte[]? expectedSettings = null;

        var environment = new Dictionary<string, string> { ["DYNAMICISLAND_TEST_DATA_ROOT"] = _dataRoot };
        var stopwatch = Stopwatch.StartNew();
        for (var run = 1; run <= Restarts; run++)
        {
            using var app = RealApp.Start(RealApp.AppExe, environment);
            try
            {
                await RealApp.WaitForMainWindowAsync(app, dismissOnboarding: false);
                await RealApp.CloseAndAssertCleanExitAsync(app);
            }
            finally
            {
                RealApp.KillIfRunning(app);
            }

            Assert.True(expectedData == await ReadDataAsync(data), $"Notes or tasks changed after run {run}.");
            var settings = await File.ReadAllBytesAsync(settingsPath, Ct);
            expectedSettings ??= settings;
            Assert.True(expectedSettings.AsSpan().SequenceEqual(settings), $"settings.json changed after run {run}.");
            Assert.Empty(Directory.GetFiles(_dataRoot, "*corrupt*", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(data, "*.tmp"));
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"{Restarts} restarts in {stopwatch.Elapsed.TotalSeconds:F0} s");
        Assert.Contains("\"Declined\"", Encoding.UTF8.GetString(expectedSettings!), StringComparison.Ordinal);
        AssertLogs();
    }

    /// <summary>No widget failed, nothing logged an error, and every run ended cleanly.</summary>
    private void AssertLogs()
    {
        var problems = new List<string>();
        var starts = 0;
        foreach (var file in Directory.GetFiles(Path.Combine(_dataRoot, "logs"), "dynamicisland-*.log"))
        {
            foreach (var line in File.ReadLines(file))
            {
                using var entry = JsonDocument.Parse(line);
                var root = entry.RootElement;
                var level = root.GetProperty("level").GetString();
                var name = root.GetProperty("event").GetString();
                var message = root.GetProperty("message").GetString() ?? string.Empty;
                if (name == "ComponentStatusChanged" && message.StartsWith("Component widget-clock: Healthy", StringComparison.Ordinal))
                {
                    starts++;
                }

                if (level is "Error" or "Critical"
                    || name == "PreviousSessionUnclean"
                    || (name == "ComponentStatusChanged" && message.Contains(": Failed (", StringComparison.Ordinal)))
                {
                    problems.Add($"{level} {name}: {message}");
                }
            }
        }

        Assert.Empty(problems);
        Assert.True(starts > 0, "The clock widget never reported Healthy.");
    }

    private static async Task SeedAsync(string data)
    {
        using var store = new SqlitePersonalDataStore(data, TimeProvider.System, NullLogger<SqlitePersonalDataStore>.Instance);
        await store.AddNoteAsync("restart note", Ct);
        await store.AddTaskAsync("open task", Ct);
        await store.AddTaskAsync("done task", Ct);
        var done = (await store.GetTasksAsync(10, Ct)).Single(t => t.Text == "done task");
        await store.SetTaskDoneAsync(done.Id, true, Ct);
        SqliteConnection.ClearAllPools();
    }

    private static async Task<string> ReadDataAsync(string data)
    {
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(data, SqlitePersonalDataStore.FileName)};Pooling=False");
        await connection.OpenAsync(Ct);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 'note|' || id || '|' || text || '|' || updated_utc FROM notes
            UNION ALL
            SELECT 'task|' || id || '|' || text || '|' || done || '|' || updated_utc || '|' || IFNULL(completed_utc, '') FROM tasks
            ORDER BY 1;
            """;
        var rows = new StringBuilder();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            rows.AppendLine(reader.GetString(0));
        }

        return rows.ToString();
    }
}
#endif
