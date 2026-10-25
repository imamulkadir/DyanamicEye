using System.Runtime.InteropServices;
using DynamicIsland.Core.Display;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using DynamicIsland.Platform.Windows.Display;

namespace DynamicIsland.WindowsSmokeTests;

/// <summary>
/// The real island window appears where the calculator says (default settings: top center of the primary work area),
/// stays topmost, is a tool window, and only its visible pixels take clicks. Measurements run under a per-monitor-v2
/// DPI scope so they are physical pixels at any scaling. In Release the app uses the real profile, which is assumed to
/// hold default appearance settings on test machines.
/// </summary>
[Trait("Category", "WindowsSmoke")]
[Collection(RealAppTests.Name)]
public sealed partial class IslandPlacementSmokeTests : IDisposable
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
        using var app = StartIsolatedApp();
        try
        {
            await RealApp.WaitForMainWindowAsync(app);
            var window = app.MainWindowHandle;

            var (expected, actual) = Measure(() =>
            {
                var defaults = new AppSettings().Appearance;
                var placement = IslandPlacementCalculator.Place(
                    new Win32MonitorService().GetMonitors(),
                    new PlacementRequest(null, defaults.Placement, defaults.HorizontalOffset, defaults.VerticalOffset, IslandLayout.Max.Width * defaults.Scale, IslandLayout.Max.Height * defaults.Scale));
                return (placement, IslandWindowInterop.GetBounds(window));
            });

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

    [Fact]
    public async Task Only_the_visible_pill_receives_clicks()
    {
        using var app = StartIsolatedApp();
        try
        {
            await RealApp.WaitForMainWindowAsync(app);
            var window = app.MainWindowHandle;

            var (center, corner, below) = Measure(() =>
            {
                // The window has the expanded size; the collapsed pill sits at its top center.
                var bounds = IslandWindowInterop.GetBounds(window);
                var scale = bounds.Width / IslandLayout.Max.Width;
                var centerX = bounds.Left + (bounds.Width / 2);
                return (
                    IslandWindowInterop.ReceivesClickAt(window, centerX, bounds.Top + (int)(IslandLayout.Collapsed.Height * scale / 2)),
                    IslandWindowInterop.ReceivesClickAt(window, bounds.Left, bounds.Top),
                    IslandWindowInterop.ReceivesClickAt(window, centerX, bounds.Bottom - 3));
            });

            Assert.True(center, "The pill itself must be clickable.");
            Assert.False(corner, "Transparent window area must let clicks through to the window below.");
            Assert.False(below, "The empty area reserved for the expanded island must not block clicks while collapsed.");

            await RealApp.CloseAndAssertCleanExitAsync(app);
        }
        finally
        {
            RealApp.KillIfRunning(app);
        }
    }

    [Theory]
    [InlineData(RealApp.AppExe)]
    [InlineData(RealApp.LauncherExe)]
    public void Executables_carry_the_app_icon(string exeName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, exeName);

        Assert.True(ExtractIconExW(path, -1, IntPtr.Zero, IntPtr.Zero, 0) >= 1, $"{exeName} has no icon resource.");
    }

    private System.Diagnostics.Process StartIsolatedApp() =>
        RealApp.Start(RealApp.AppExe, new Dictionary<string, string> { ["DYNAMICISLAND_TEST_DATA_ROOT"] = _dataRoot });

    /// <summary>Runs synchronously on one thread with physical-pixel coordinates.</summary>
    private static T Measure<T>(Func<T> measure)
    {
        using var scope = DpiAwarenessScope.PerMonitorV2();
        return measure();
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

    /// <summary>With <c>iconIndex = -1</c> returns the number of icons in the file.</summary>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint ExtractIconExW(string file, int iconIndex, IntPtr largeIcons, IntPtr smallIcons, uint count);
}
