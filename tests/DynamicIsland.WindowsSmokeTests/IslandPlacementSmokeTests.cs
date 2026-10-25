using DynamicIsland.Core.Display;
using DynamicIsland.Core.Settings;
using DynamicIsland.Platform.Windows.Display;

namespace DynamicIsland.WindowsSmokeTests;

/// <summary>
/// The real island window appears where the calculator says (default settings: top center of the primary work area),
/// stays topmost and is a tool window (no taskbar button). In Release the app uses the real profile, which is
/// assumed to hold default appearance settings on test machines.
/// </summary>
[Trait("Category", "WindowsSmoke")]
[Collection(RealAppTests.Name)]
public sealed class IslandPlacementSmokeTests : IDisposable
{
    private const int TolerancePixels = 2;

    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "DynamicIsland.SmokeTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Island_is_a_topmost_tool_window_at_top_center_of_the_primary_work_area()
    {
        using var app = RealApp.Start(RealApp.AppExe, new Dictionary<string, string> { ["DYNAMICISLAND_TEST_DATA_ROOT"] = _dataRoot });
        try
        {
            await RealApp.WaitForMainWindowAsync(app);
            var window = app.MainWindowHandle;

            var defaults = new AppSettings().Appearance;
            var expected = IslandPlacementCalculator.Place(
                new Win32MonitorService().GetMonitors(),
                new PlacementRequest(null, defaults.Placement, defaults.HorizontalOffset, defaults.VerticalOffset, 200 * defaults.Scale, 36 * defaults.Scale));
            var actual = IslandWindowInterop.GetBounds(window);

            Assert.True(expected.Monitor.IsPrimary);
            AssertClose(expected.Rect, actual);
            Assert.True(expected.Monitor.WorkArea.Contains(actual), $"Island {actual} must be inside the work area {expected.Monitor.WorkArea}.");
            Assert.True(IslandWindowInterop.IsToolWindow(window), "Island must not have a taskbar button.");
            Assert.True(IslandWindowInterop.IsTopmost(window), "Island must stay above normal windows.");

            await RealApp.CloseAndAssertCleanExitAsync(app);
        }
        finally
        {
            RealApp.KillIfRunning(app);
        }
    }

    private static void AssertClose(PixelRect expected, PixelRect actual)
    {
        Assert.True(
            Math.Abs(expected.Left - actual.Left) <= TolerancePixels
            && Math.Abs(expected.Top - actual.Top) <= TolerancePixels
            && Math.Abs(expected.Width - actual.Width) <= TolerancePixels
            && Math.Abs(expected.Height - actual.Height) <= TolerancePixels,
            $"Expected {expected}, actual {actual}.");
    }
}
