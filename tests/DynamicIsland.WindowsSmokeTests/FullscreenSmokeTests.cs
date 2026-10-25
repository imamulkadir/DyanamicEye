using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using DynamicIsland.Platform.Windows.Display;
using DynamicIsland.TestSupport;

namespace DynamicIsland.WindowsSmokeTests;

/// <summary>
/// End to end: a borderless window covering the island's monitor becomes the foreground window → the real island hides;
/// closing it → the island comes back. Windows only lets a process take the foreground in some situations; if the
/// helper window cannot become foreground on this host the test is skipped (reported, not passed).
/// </summary>
[Trait("Category", "WindowsSmoke")]
[Collection(RealAppTests.Name)]
public sealed class FullscreenSmokeTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "DynamicIsland.SmokeTests", Guid.NewGuid().ToString("N"));
    private readonly StaDispatcherThread _ui = new();

    public void Dispose()
    {
        _ui.Dispose();
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Island_hides_while_a_fullscreen_window_is_in_front_and_returns_afterwards()
    {
        using var app = RealApp.Start(RealApp.AppExe, new Dictionary<string, string> { ["DYNAMICISLAND_TEST_DATA_ROOT"] = _dataRoot });
        Window? helper = null;
        try
        {
            await RealApp.WaitForMainWindowAsync(app);
            var island = app.MainWindowHandle;

            var primary = Measure(() => new Win32MonitorService().GetMonitors().Single(m => m.IsPrimary));
            var helperHandle = _ui.Invoke(() =>
            {
                using var scope = DpiAwarenessScope.PerMonitorV2();
                helper = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Title = "Fullscreen smoke test" };
                helper.Show();
                var hwnd = new WindowInteropHelper(helper).Handle;
                IslandWindowInterop.SetBounds(hwnd, primary.Bounds, topmost: false);
                helper.Activate();
                return hwnd;
            });

            if (!await WaitUntilAsync(() => IslandWindowInterop.IsForeground(helperHandle)))
            {
                Assert.Skip("Windows did not allow the test window to become the foreground window on this host.");
            }

            Assert.True(await WaitUntilAsync(() => !IslandWindowInterop.IsVisible(island)), "Island must hide behind a fullscreen app on its monitor.");

            _ui.Invoke(() => helper!.Close());
            helper = null;

            Assert.True(await WaitUntilAsync(() => IslandWindowInterop.IsVisible(island)), "Island must return when the fullscreen app closes.");
            await RealApp.CloseAndAssertCleanExitAsync(app);
        }
        finally
        {
            if (helper is not null)
            {
                _ui.Invoke(() => helper.Close());
            }

            RealApp.KillIfRunning(app);
        }
    }

    private static T Measure<T>(Func<T> measure)
    {
        using var scope = DpiAwarenessScope.PerMonitorV2();
        return measure();
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

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        return true;
    }
}
