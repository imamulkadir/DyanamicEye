using System.Diagnostics;
using System.Windows.Automation;
using DynamicIsland.Platform.Windows.Display;
using DynamicIsland.Platform.Windows.Tray;

namespace DynamicIsland.WindowsSmokeTests;

/// <summary>
/// Drives the real app's tray menu like an assistive technology would: opens it through the icon's callback message
/// (what a right click sends) and invokes items by AutomationId — no synthetic mouse input on the user's desktop.
/// </summary>
internal static class TrayMenuDriver
{
    private const string TrayWindowTitle = "DynamicIsland.Tray";
    private const uint TrayIconId = 1;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <param name="path">AutomationIds from the top menu down; all but the last are submenus to expand.</param>
    public static async Task InvokeAsync(Process app, params string[] path)
    {
        var owner = await WaitForAsync(() => IslandWindowInterop.FindTopLevelWindow(app.Id, TrayWindowTitle), h => h != IntPtr.Zero, "tray owner window");
        Win32TrayIcon.RequestContextMenu(owner, TrayIconId);

        for (var i = 0; i < path.Length; i++)
        {
            var id = path[i];
            var item = await WaitForAsync(() => Find(app.Id, id), e => e is not null, $"menu item {id}");
            if (i < path.Length - 1)
            {
                ((ExpandCollapsePattern)item!.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
            }
            else
            {
                ((InvokePattern)item!.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            }
        }
    }

    private static AutomationElement? Find(int processId, string automationId) =>
        AutomationElement.RootElement.FindFirst(
            TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(AutomationElement.ProcessIdProperty, processId),
                new PropertyCondition(AutomationElement.AutomationIdProperty, automationId)));

    private static async Task<T> WaitForAsync<T>(Func<T> probe, Func<T, bool> done, string what)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var value = probe();
            if (done(value))
            {
                return value;
            }

            Assert.True(stopwatch.Elapsed < Timeout, $"Timed out waiting for {what}.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }
}
