using System.Diagnostics;
using System.Windows.Automation;
using DynamicIsland.Core.Shell;
using DynamicIsland.Platform.Windows.Display;

namespace DynamicIsland.WindowsSmokeTests;

/// <summary>
/// Drives the real island through UI Automation (the same path a screen reader uses) without moving the mouse,
/// and checks the visible pill's size per state and that the window itself never changes size.
/// </summary>
[Trait("Category", "WindowsSmoke")]
[Collection(RealAppTests.Name)]
public sealed class IslandInteractionSmokeTests : IDisposable
{
    private const int TolerancePixels = 3;
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(5);

    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "DynamicIsland.SmokeTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Expand_pin_and_collapse_through_ui_automation()
    {
        using var app = RealApp.Start(RealApp.AppExe, new Dictionary<string, string> { ["DYNAMICISLAND_TEST_DATA_ROOT"] = _dataRoot });
        try
        {
            await RealApp.WaitForMainWindowAsync(app);
            var window = app.MainWindowHandle;
            var windowBefore = Measure(() => IslandWindowInterop.GetBounds(window));
            var scale = windowBefore.Width / IslandLayout.Max.Width;

            var pill = Measure(() => AutomationElement.FromHandle(window).FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "IslandSurface")));
            Assert.NotNull(pill);
            var expandCollapse = (ExpandCollapsePattern)pill.GetCurrentPattern(ExpandCollapsePattern.Pattern);
            var toggle = (TogglePattern)pill.GetCurrentPattern(TogglePattern.Pattern);

            await WaitForPillSizeAsync(pill, IslandLayout.Collapsed, scale);
            Assert.Equal(ExpandCollapseState.Collapsed, expandCollapse.Current.ExpandCollapseState);

            expandCollapse.Expand();
            await WaitForExpandedPillAsync(pill, scale);
            Assert.Equal(ExpandCollapseState.Expanded, expandCollapse.Current.ExpandCollapseState);

            toggle.Toggle();
            await WaitUntilAsync(() => toggle.Current.ToggleState == ToggleState.On);

            expandCollapse.Collapse();
            await WaitForPillSizeAsync(pill, IslandLayout.Collapsed, scale);
            Assert.Equal(ToggleState.Off, toggle.Current.ToggleState);
            Assert.Equal(ExpandCollapseState.Collapsed, expandCollapse.Current.ExpandCollapseState);

            Assert.Equal(windowBefore, Measure(() => IslandWindowInterop.GetBounds(window)));
            Assert.True(IslandWindowInterop.IsVisible(window));

            await RealApp.CloseAndAssertCleanExitAsync(app);
        }
        finally
        {
            RealApp.KillIfRunning(app);
        }
    }

    private static async Task WaitForPillSizeAsync(AutomationElement pill, (double Width, double Height) dips, double scale)
    {
        var expected = (Width: dips.Width * scale, Height: dips.Height * scale);
        System.Windows.Rect bounds = default;
        await WaitUntilAsync(() =>
        {
            bounds = Measure(() => pill.Current.BoundingRectangle);
            return Math.Abs(bounds.Width - expected.Width) <= TolerancePixels && Math.Abs(bounds.Height - expected.Height) <= TolerancePixels;
        }, $"pill size {expected.Width}x{expected.Height}");
    }

    /// <summary>Expanded height follows the widgets' content (ADR 0022): full width, height within the allowed range.</summary>
    private static async Task WaitForExpandedPillAsync(AutomationElement pill, double scale)
    {
        System.Windows.Rect bounds = default;
        await WaitUntilAsync(() =>
        {
            bounds = Measure(() => pill.Current.BoundingRectangle);
            return Math.Abs(bounds.Width - IslandLayout.Expanded.Width * scale) <= TolerancePixels
                && bounds.Height >= IslandLayout.Expanded.Height * scale - TolerancePixels
                && bounds.Height <= IslandLayout.ExpandedMaxHeight * scale + TolerancePixels;
        }, $"expanded pill {IslandLayout.Expanded.Width * scale} wide, {IslandLayout.Expanded.Height * scale}–{IslandLayout.ExpandedMaxHeight * scale} high");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what = "condition")
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(stopwatch.Elapsed < SettleTimeout, $"Timed out waiting for {what}.");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    private static T Measure<T>(Func<T> measure)
    {
        using var scope = DpiAwarenessScope.PerMonitorV2();
        return measure();
    }
}
