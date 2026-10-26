#if DYNAMICISLAND_TEST_HOOKS
using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Interop;
using DynamicIsland.Core.Shell;
using DynamicIsland.Platform.Windows.Display;
using DynamicIsland.Platform.Windows.Input;
using DynamicIsland.TestSupport;

namespace DynamicIsland.WindowsSmokeTests;

/// <summary>
/// The global shortcut with the real app (P03-05, ADR 0017). Debug only (isolated settings). No key presses are
/// injected: the test proves Windows granted the registration (a second registration is refused) and delivers the
/// <c>WM_HOTKEY</c> message Windows would post.
/// </summary>
[Trait("Category", "WindowsSmoke")]
[Collection(RealAppTests.Name)]
public sealed class ShortcutSmokeTests : IDisposable
{
    private const string Keys = "Ctrl+Alt+Shift+F21";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "DynamicIsland.SmokeTests", Guid.NewGuid().ToString("N"));
    private readonly DateTime _testStarted = DateTime.Now;
    private readonly StaDispatcherThread _ui = new();

    public void Dispose()
    {
        _ui.Dispose();
        RealApp.KillStrayApps(_testStarted);
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Shortcut_is_registered_with_windows_and_toggles_the_island()
    {
        var data = Directory.CreateDirectory(Path.Combine(_dataRoot, "data"));
        await File.WriteAllTextAsync(
            Path.Combine(data.FullName, "settings.json"),
            $$"""{ "schemaVersion": 1, "behavior": { "shortcut": { "enabled": true, "keys": "{{Keys}}" } } }""",
            TestContext.Current.CancellationToken);

        using var app = RealApp.Start(RealApp.AppExe, new Dictionary<string, string> { ["DYNAMICISLAND_TEST_DATA_ROOT"] = _dataRoot });
        try
        {
            await RealApp.WaitForMainWindowAsync(app);

            Assert.Equal(HotkeyRegistration.InUse, TryRegisterHere());

            var target = IslandWindowInterop.FindMessageWindow(app.Id, "DynamicIsland.Shortcut");
            Assert.NotEqual(IntPtr.Zero, target);

            IslandWindowInterop.Post(target, Win32Hotkey.WmHotkey, 1, IntPtr.Zero);
            Assert.True(await WaitForIslandNameAsync(app.Id, "Dynamic Island, expanded"), "The shortcut must open the island.");

            IslandWindowInterop.Post(target, Win32Hotkey.WmHotkey, 1, IntPtr.Zero);
            Assert.True(await WaitForIslandNameAsync(app.Id, "Dynamic Island, collapsed"), "Pressing it again must close the island.");

            await RealApp.CloseAndAssertCleanExitAsync(app);
            Assert.Equal(HotkeyRegistration.Registered, TryRegisterHere()); // Released on exit.
        }
        finally
        {
            RealApp.KillIfRunning(app);
        }
    }

    /// <summary>Registers the same combination from this process and releases it at once.</summary>
    private HotkeyRegistration TryRegisterHere() => _ui.Invoke(() =>
    {
        Assert.True(Hotkey.TryParse(Keys, out var hotkey, out _));
        using var window = new HwndSource(new HwndSourceParameters("ShortcutSmokeTests") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
        using var registration = new Win32Hotkey(window.Handle, 1);
        return registration.Register(hotkey!);
    });

    private static async Task<bool> WaitForIslandNameAsync(int processId, string name)
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
                return true;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return false;
    }
}
#endif
