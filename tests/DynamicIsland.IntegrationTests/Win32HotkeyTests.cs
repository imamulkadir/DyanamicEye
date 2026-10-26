using System.Windows.Interop;
using DynamicIsland.Core.Shell;
using DynamicIsland.Platform.Windows.Input;
using DynamicIsland.TestSupport;

namespace DynamicIsland.IntegrationTests;

/// <summary>
/// Real <c>RegisterHotKey</c> (ADR 0017) with an unusual combination that nobody uses, registered for milliseconds and
/// always released.
/// </summary>
public sealed class Win32HotkeyTests : IDisposable
{
    private readonly StaDispatcherThread _ui = new();

    public void Dispose() => _ui.Dispose();

    [Fact]
    public void Registers_reports_a_conflict_and_releases()
    {
        var results = _ui.Invoke(() =>
        {
            Assert.True(Hotkey.TryParse("Ctrl+Alt+Shift+F23", out var hotkey, out _));
            using var first = MessageWindow();
            using var second = MessageWindow();
            using var owner = new Win32Hotkey(first.Handle, 7);
            using var rival = new Win32Hotkey(second.Handle, 7);

            var registered = owner.Register(hotkey!);
            var conflict = rival.Register(hotkey!);
            owner.Unregister();
            var afterRelease = rival.Register(hotkey!);
            rival.Unregister();
            return (registered, conflict, afterRelease, owner.Current);
        });

        Assert.Equal(HotkeyRegistration.Registered, results.registered);
        Assert.Equal(HotkeyRegistration.InUse, results.conflict);
        Assert.Equal(HotkeyRegistration.Registered, results.afterRelease);
        Assert.Null(results.Current);
    }

    [Fact]
    public void Only_its_own_hotkey_message_counts()
    {
        var (own, other, notHotkey) = _ui.Invoke(() =>
        {
            Assert.True(Hotkey.TryParse("Ctrl+Alt+Shift+F22", out var hotkey, out _));
            using var window = MessageWindow();
            using var registration = new Win32Hotkey(window.Handle, 3);
            registration.Register(hotkey!);
            return (registration.IsPressed(Win32Hotkey.WmHotkey, 3), registration.IsPressed(Win32Hotkey.WmHotkey, 4), registration.IsPressed(0x0100, 3));
        });

        Assert.True(own);
        Assert.False(other);
        Assert.False(notHotkey);
    }

    private static HwndSource MessageWindow() => new(new HwndSourceParameters("Win32HotkeyTests") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
}
