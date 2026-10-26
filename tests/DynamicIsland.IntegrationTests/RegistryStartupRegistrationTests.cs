using DynamicIsland.Platform.Windows.Startup;
using Microsoft.Win32;

namespace DynamicIsland.IntegrationTests;

/// <summary>
/// Real registry, but under a scratch key in HKCU that stands for <c>Software\Microsoft\Windows\CurrentVersion</c>
/// (ADR 0015 §7): the user's real Run entries are never touched.
/// </summary>
public sealed class RegistryStartupRegistrationTests : IDisposable
{
    private const string Command = "\"C:\\Apps\\DynamicIsland\\DynamicIsland.Launcher.exe\" --startup";

    private readonly string _root = @"Software\DynamicIsland.Tests\" + Guid.NewGuid().ToString("N");

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);

    [Fact]
    public void Register_writes_the_run_value_and_unregister_removes_only_it()
    {
        var registration = new RegistryStartupRegistration(_root, checkMachinePolicy: false);
        using (var run = Registry.CurrentUser.CreateSubKey(_root + @"\Run"))
        {
            run.SetValue("OtherApp", "other.exe");
        }

        Assert.False(registration.GetStatus().IsRegistered);
        registration.Register(Command);

        Assert.Equal(Command, registration.GetStatus().Command);
        using (var run = Registry.CurrentUser.OpenSubKey(_root + @"\Run")!)
        {
            Assert.Equal(RegistryValueKind.String, run.GetValueKind(RegistryStartupRegistration.DefaultValueName));
        }

        registration.Unregister();
        registration.Unregister(); // Absent value: no error.

        Assert.False(registration.GetStatus().IsRegistered);
        using var after = Registry.CurrentUser.OpenSubKey(_root + @"\Run")!;
        Assert.Equal("other.exe", after.GetValue("OtherApp"));
    }

    [Theory]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x03, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }, true)]
    [InlineData(new byte[] { 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x07, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    public void Windows_startup_switch_is_read_and_left_untouched(byte[] approved, bool disabled)
    {
        var registration = new RegistryStartupRegistration(_root, checkMachinePolicy: false);
        using (var key = Registry.CurrentUser.CreateSubKey(_root + @"\Explorer\StartupApproved\Run"))
        {
            key.SetValue(RegistryStartupRegistration.DefaultValueName, approved, RegistryValueKind.Binary);
        }

        registration.Register(Command);
        Assert.Equal(disabled, registration.GetStatus().DisabledInWindows);
        registration.Unregister();

        using var after = Registry.CurrentUser.OpenSubKey(_root + @"\Explorer\StartupApproved\Run")!;
        Assert.Equal(approved, (byte[])after.GetValue(RegistryStartupRegistration.DefaultValueName)!);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void Per_user_run_policy_is_reported(int policy, bool blocked)
    {
        var registration = new RegistryStartupRegistration(_root, checkMachinePolicy: false);
        using (var key = Registry.CurrentUser.CreateSubKey(_root + @"\Policies\Explorer"))
        {
            key.SetValue("DisableCurrentUserRun", policy, RegistryValueKind.DWord);
        }

        Assert.Equal(blocked, registration.GetStatus().BlockedByPolicy);
    }
}
