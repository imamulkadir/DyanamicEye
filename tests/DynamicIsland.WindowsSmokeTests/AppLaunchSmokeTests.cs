namespace DynamicIsland.WindowsSmokeTests;

[Trait("Category", "WindowsSmoke")]
[Collection(RealAppTests.Name)]
public sealed class AppLaunchSmokeTests
{
    [Fact]
    public async Task App_starts_shows_a_window_and_exits_cleanly_when_closed()
    {
        using var app = RealApp.Start(RealApp.AppExe);
        try
        {
            await RealApp.WaitForMainWindowAsync(app);

            await RealApp.CloseAndAssertCleanExitAsync(app);
        }
        finally
        {
            RealApp.KillIfRunning(app);
        }
    }

    [Fact]
    public async Task Second_launch_exits_quietly_and_leaves_the_first_instance_running()
    {
        using var first = RealApp.Start(RealApp.AppExe);
        try
        {
            await RealApp.WaitForMainWindowAsync(first);

            using var second = RealApp.Start(RealApp.AppExe);
            try
            {
                await RealApp.WaitForExitAsync(second, RealApp.ExitTimeout);
                Assert.Equal(0, second.ExitCode);
                Assert.Equal(IntPtr.Zero, second.MainWindowHandle);
            }
            finally
            {
                RealApp.KillIfRunning(second);
            }

            Assert.False(first.HasExited, "The first instance must keep running.");
            await RealApp.CloseAndAssertCleanExitAsync(first);
        }
        finally
        {
            RealApp.KillIfRunning(first);
        }
    }
}
