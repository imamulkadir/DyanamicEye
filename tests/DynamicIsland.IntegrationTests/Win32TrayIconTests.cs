using System.Windows.Interop;
using DynamicIsland.Platform.Windows.Tray;
using DynamicIsland.TestSupport;

namespace DynamicIsland.IntegrationTests;

/// <summary>Real notification-area registration on the test host (skipped where there is no shell tray).</summary>
public sealed class Win32TrayIconTests : IDisposable
{
    private const uint TestIconId = 7;

    private readonly StaDispatcherThread _ui = new();

    public void Dispose() => _ui.Dispose();

    [Fact]
    public void Icon_is_registered_survives_an_explorer_restart_and_is_removed_on_dispose()
    {
        if (!Win32TrayIcon.IsShellTrayAvailable)
        {
            Assert.Skip("No shell notification area on this host.");
        }

        var (afterAdd, afterRestart, afterDispose) = _ui.Invoke(() =>
        {
            using var window = new HwndSource(new HwndSourceParameters("DynamicIsland.Tests.Tray") { Width = 0, Height = 0, WindowStyle = 0 });
            var icon = new Win32TrayIcon(window.Handle, TestIconId, "Dynamic Island tray test");
            window.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                icon.HandleMessage(message, wParam, lParam);
                return IntPtr.Zero;
            });

            Assert.True(icon.Add(), "Shell_NotifyIcon(NIM_ADD) failed.");
            var added = icon.IsRegisteredWithShell();

            // Explorer restart: the shell forgets every icon, then broadcasts TaskbarCreated.
            icon.Remove();
            Win32TrayIcon.SendTaskbarCreated(window.Handle);
            var restored = icon.IsRegisteredWithShell();

            icon.Dispose();
            return (added, restored, icon.IsRegisteredWithShell());
        });

        Assert.True(afterAdd, "Icon must be registered after Add.");
        Assert.True(afterRestart, "Icon must re-register itself after TaskbarCreated (Explorer restart).");
        Assert.False(afterDispose, "Icon must be removed on dispose.");
    }

    [Theory]
    [InlineData(0x0400, TrayIconEvent.Select)]
    [InlineData(0x0401, TrayIconEvent.KeySelect)]
    [InlineData(0x007B, TrayIconEvent.ContextMenu)]
    [InlineData(0x0405, TrayIconEvent.NotificationSelect)] // NIN_BALLOONUSERCLICK: the update notification (ADR 0033).
    [InlineData(0x0200, TrayIconEvent.None)]
    public void Version4_callback_events_are_mapped(int notification, TrayIconEvent expected)
    {
        var icon = new Win32TrayIcon(IntPtr.Zero, TestIconId, "unused");

        // NOTIFYICON_VERSION_4: LOWORD(lParam) = event, HIWORD(lParam) = icon id.
        var lParam = new IntPtr(((int)TestIconId << 16) | notification);

        Assert.Equal(expected, icon.HandleMessage(Win32TrayIcon.CallbackMessage, IntPtr.Zero, lParam));
    }

    [Fact]
    public void Unrelated_messages_are_ignored()
    {
        var icon = new Win32TrayIcon(IntPtr.Zero, TestIconId, "unused");

        Assert.Equal(TrayIconEvent.None, icon.HandleMessage(0x0010, IntPtr.Zero, IntPtr.Zero));
    }
}
