namespace DynamicIsland.WindowsSmokeTests;

/// <summary>
/// D10 activity budget: a collapsed, idle island does no periodic work (no animations, timers or polling), so its CPU
/// time barely moves. The threshold is generous for slow CI runners; the dev host measures the real figure.
/// </summary>
[Trait("Category", "WindowsSmoke")]
[Collection(RealAppTests.Name)]
public sealed class IdleBudgetSmokeTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxCpu = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task Collapsed_idle_island_uses_almost_no_cpu()
    {
        using var app = RealApp.Start(RealApp.AppExe);
        try
        {
            await RealApp.WaitForMainWindowAsync(app);
            await Task.Delay(Settle, TestContext.Current.CancellationToken);

            app.Refresh();
            var cpuBefore = app.TotalProcessorTime;
            await Task.Delay(Window, TestContext.Current.CancellationToken);
            app.Refresh();
            var cpu = app.TotalProcessorTime - cpuBefore;

            TestContext.Current.SendDiagnosticMessage($"Idle CPU over {Window.TotalSeconds}s: {cpu.TotalMilliseconds:F0} ms; working set {app.WorkingSet64 / (1024 * 1024)} MiB.");
            Assert.True(cpu < MaxCpu, $"Idle island used {cpu.TotalMilliseconds:F0} ms CPU in {Window.TotalSeconds}s (limit {MaxCpu.TotalMilliseconds} ms).");

            await RealApp.CloseAndAssertCleanExitAsync(app);
        }
        finally
        {
            RealApp.KillIfRunning(app);
        }
    }
}
